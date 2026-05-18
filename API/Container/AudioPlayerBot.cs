using EviAudio.Other;
using Exiled.API.Features;
using MEC;
using PlayerRoles;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using VoiceChat;
using Object = UnityEngine.Object;
using GameIntercom = PlayerRoles.Voice.Intercom;

namespace EviAudio.API.Container;

public sealed class AudioPlayerBot
{
    private BotAudioStreamer _streamer;
    private CoroutineHandle _graceHandle;
    private CoroutineHandle _followHandle;
    private bool _graceScheduled;

    public int ID { get; }
    public string Name { get; private set; }
    public Player Player { get; private set; }

    public bool IsPlaying => _streamer != null && _streamer.IsPlaying;
    public bool IsSpawned => Player is Npc npc && npc.IsConnected;
    public TimeSpan Position => _streamer?.Position ?? TimeSpan.Zero;
    public TimeSpan Duration => _streamer?.Duration ?? TimeSpan.Zero;
    public AudioTrackMetadata CurrentMetadata => _streamer?.CurrentMetadata ?? new AudioTrackMetadata();

    public event Action<string> OnTrackFinished
    {
        add { if (_streamer != null) _streamer.OnTrackFinished += value; }
        remove { if (_streamer != null) _streamer.OnTrackFinished -= value; }
    }

    public VoiceChatChannel VoiceChatChannel
    {
        get => _streamer?.Channel ?? VoiceChatChannel.Intercom;
        set { if (_streamer != null) _streamer.Channel = value; }
    }

    public float Volume
    {
        get => _streamer?.Volume ?? 100f;
        set { if (_streamer != null) _streamer.Volume = value; }
    }

    public bool Loop
    {
        get => _streamer?.Loop ?? false;
        set { if (_streamer != null) _streamer.Loop = value; }
    }

    public bool Continue
    {
        get => _streamer?.ContinueAfterTrack ?? false;
        set { if (_streamer != null) _streamer.ContinueAfterTrack = value; }
    }

    public bool Shuffle
    {
        get => _streamer?.Shuffle ?? false;
        set { if (_streamer != null) _streamer.Shuffle = value; }
    }

    public bool IsPaused
    {
        get => _streamer?.IsPaused ?? false;
        set { if (_streamer != null) _streamer.IsPaused = value; }
    }

    public float PitchShift
    {
        get => _streamer?.PitchShift ?? 0f;
        set { if (_streamer != null) _streamer.PitchShift = value; }
    }

    public string CurrentTrack => _streamer?.CurrentPlay ?? string.Empty;
    public HashSet<int> BroadcastTo => _streamer?.BroadcastTo;
    public Dictionary<int, float> PlayerVolumes => _streamer?.PlayerVolumes;
    public Func<ReferenceHub, bool> Condition
    {
        get => _streamer?.Condition;
        set { if (_streamer != null) _streamer.Condition = value; }
    }

    private AudioPlayerBot(int id, string name, BotAudioStreamer streamer, Player player)
    {
        ID = id;
        Name = name;
        _streamer = streamer;
        Player = player;
    }

    public static AudioPlayerBot SpawnDummy(
        string name = "[BOT] EviAudio",
        int id = 99,
        RoleTypeId role = RoleTypeId.Tutorial,
        bool ignored = true)
    {
        if (Plugin.AudioPlayerList.TryGetValue(id, out var existing))
        {
            Log.Debug($"Bot ID={id} already exists.");
            return existing;
        }

        if (!ControllerIdPool.TryReserve(id, $"bot:{name}"))
        {
            Log.Error($"Bot ID={id} conflicts with another audio controller.");
            return null;
        }

        try
        {
            Npc npc = Npc.Spawn(name, role, ignored);
            ApplyNickname(npc, name);
            Timing.CallDelayed(1f, () => ApplyNickname(npc, name));

            var go = new GameObject($"EviAudio.Bot.{id}");
            go.hideFlags = HideFlags.DontUnloadUnusedAsset;
            var streamer = go.AddComponent<BotAudioStreamer>();
            streamer.Init(npc.ReferenceHub);

            var bot = new AudioPlayerBot(id, name, streamer, npc);
            if (!Plugin.AudioPlayerList.TryAdd(id, bot))
            {
                Object.Destroy(streamer.gameObject);
                ControllerIdPool.Release(id);
                Log.Error($"Bot ID={id} was reserved concurrently.");
                return Plugin.AudioPlayerList.TryGetValue(id, out var raced) ? raced : null;
            }

            Log.Debug($"Bot '{name}' (ID={id}) spawned.");
            return bot;
        }
        catch
        {
            ControllerIdPool.Release(id);
            throw;
        }
    }

    public void PlayFile(
        string filePath,
        float volume = 100f,
        bool loop = false,
        VoiceChatChannel? channel = null,
        IEnumerable<int> targetPlayerIds = null,
        bool shuffle = false,
        bool continueQueue = false,
        TimeSpan? startAt = null,
        TimeSpan? endAt = null)
    {
        if (_streamer == null)
        {
            Log.Error($"PlayFile: streamer is null for bot '{Name}' (ID={ID}).");
            return;
        }

        string resolvedPath = PcmDecoder.IsUrl(filePath) ? filePath : Extensions.PathCheck(filePath);
        if (!PcmDecoder.IsUrl(resolvedPath) && !File.Exists(resolvedPath))
        {
            Log.Warn($"File not found: {resolvedPath}");
            return;
        }

        volume = AudioMath.Clamp(volume, 0f, 100f);

        if (ShouldDelayForGrace(resolvedPath, volume, loop, channel, targetPlayerIds, shuffle, continueQueue, startAt, endAt))
            return;

        _streamer.Stop(clearQueue: true);

        if (channel.HasValue) _streamer.Channel = channel.Value;
        if (_streamer.Channel == VoiceChatChannel.Intercom && Player?.ReferenceHub != null)
            GameIntercom.TrySetOverride(Player.ReferenceHub, true);

        _streamer.Volume = volume;
        _streamer.Loop = loop;
        _streamer.Shuffle = shuffle;
        _streamer.ContinueAfterTrack = continueQueue;

        _streamer.BroadcastTo.Clear();
        if (targetPlayerIds != null)
            foreach (int pid in targetPlayerIds)
                _streamer.BroadcastTo.Add(pid);

        _streamer.Enqueue(resolvedPath);
        _streamer.Play(startAt, endAt);

        Log.Debug($"▶ {Path.GetFileName(resolvedPath)} ch={_streamer.Channel} vol={volume} loop={loop}");
    }

    public void PlayFolder(
        string folderPath,
        float volume = 100f,
        bool shuffle = false,
        VoiceChatChannel? channel = null,
        IEnumerable<int> targetPlayerIds = null)
    {
        if (_streamer == null) return;

        string resolvedFolder = Directory.Exists(folderPath)
            ? folderPath
            : Path.Combine(Plugin.Instance.AudioPath, folderPath);

        if (!Directory.Exists(resolvedFolder))
        {
            Log.Warn($"PlayFolder: folder not found '{resolvedFolder}'.");
            return;
        }

        _streamer.Stop(clearQueue: true);

        if (channel.HasValue) _streamer.Channel = channel.Value;
        if (_streamer.Channel == VoiceChatChannel.Intercom && Player?.ReferenceHub != null)
            GameIntercom.TrySetOverride(Player.ReferenceHub, true);

        _streamer.Volume = AudioMath.Clamp(volume, 0f, 100f);
        _streamer.ContinueAfterTrack = true;
        _streamer.Shuffle = shuffle;

        _streamer.BroadcastTo.Clear();
        if (targetPlayerIds != null)
            foreach (int pid in targetPlayerIds)
                _streamer.BroadcastTo.Add(pid);

        _streamer.EnqueueFolder(resolvedFolder, shuffle);
        _streamer.Play();

        Log.Debug($"▶ folder '{resolvedFolder}' shuffle={shuffle}");
    }

    public void PlayM3U(
        string m3uPath,
        float volume = 100f,
        VoiceChatChannel? channel = null,
        IEnumerable<int> targetPlayerIds = null)
    {
        if (_streamer == null) return;

        string resolved = Extensions.PathCheck(m3uPath);
        if (!File.Exists(resolved))
        {
            Log.Warn($"PlayM3U: file not found '{resolved}'.");
            return;
        }

        _streamer.Stop(clearQueue: true);

        if (channel.HasValue) _streamer.Channel = channel.Value;
        if (_streamer.Channel == VoiceChatChannel.Intercom && Player?.ReferenceHub != null)
            GameIntercom.TrySetOverride(Player.ReferenceHub, true);

        _streamer.Volume = AudioMath.Clamp(volume, 0f, 100f);
        _streamer.ContinueAfterTrack = true;

        _streamer.BroadcastTo.Clear();
        if (targetPlayerIds != null)
            foreach (int pid in targetPlayerIds)
                _streamer.BroadcastTo.Add(pid);

        _streamer.EnqueueM3U(resolved);
        _streamer.Play();
    }

    public void Enqueue(string filePath, int position = -1)
    {
        string resolvedPath = PcmDecoder.IsUrl(filePath) ? filePath : Extensions.PathCheck(filePath);
        if (!PcmDecoder.IsUrl(resolvedPath) && !File.Exists(resolvedPath))
        {
            Log.Warn($"Enqueue: file not found '{resolvedPath}'.");
            return;
        }

        _streamer?.Enqueue(resolvedPath, position);
    }

    public void InsertNext(string filePath)
    {
        string resolvedPath = PcmDecoder.IsUrl(filePath) ? filePath : Extensions.PathCheck(filePath);
        if (!PcmDecoder.IsUrl(resolvedPath) && !File.Exists(resolvedPath))
        {
            Log.Warn($"InsertNext: file not found '{resolvedPath}'.");
            return;
        }

        _streamer?.InsertNext(resolvedPath);
    }

    public void Skip() => _streamer?.Skip();

    public List<string> GetQueue() => _streamer?.GetQueue() ?? new List<string>();

    public void ClearQueue() => _streamer?.ClearQueue();

    public void ShuffleQueue() => _streamer?.ShuffleQueue();

    public bool MoveQueueItem(int from, int to) => _streamer?.MoveQueueItem(from, to) ?? false;

    public bool RemoveQueueAt(int index) => _streamer?.RemoveQueueAt(index) ?? false;

    public int RemoveQueueByPath(string path) => _streamer?.RemoveQueueByPath(path) ?? 0;

    public void FadeTo(float targetVolume, float duration) => _streamer?.FadeTo(targetVolume, duration);

    public bool SeekTo(TimeSpan position) => _streamer?.SeekTo(position) ?? false;

    public void SetPlayerVolume(int playerId, float volume) => _streamer?.SetPlayerVolume(playerId, volume);

    public void RemovePlayerData(int playerId) => _streamer?.RemovePlayerData(playerId);

    public int GetListenerCount() => _streamer?.GetListenerCount() ?? 0;

    public void CrossfadeTo(string filePath, float volume = 100f, bool loop = false)
    {
        if (_streamer == null) return;

        string resolved = PcmDecoder.IsUrl(filePath) ? filePath : Extensions.PathCheck(filePath);
        if (!PcmDecoder.IsUrl(resolved) && !File.Exists(resolved))
        {
            Log.Warn($"CrossfadeTo: file not found '{resolved}'.");
            return;
        }

        _streamer.CrossfadeTo(resolved, AudioMath.Clamp(volume, 0f, 100f), loop);
    }

    public void FollowPlayer(Player target, float interval = 0.1f)
    {
        StopFollowing();

        if (target == null)
            return;

        _followHandle = Timing.RunCoroutine(FollowCoroutine(target, Math.Max(0.02f, interval)));
    }

    public void StopFollowing()
    {
        if (_followHandle.IsRunning)
            Timing.KillCoroutines(_followHandle);
    }

    public void PlayIntercom(string filePath, float volume = 100f, bool loop = false)
        => PlayFile(filePath, volume, loop, VoiceChatChannel.Intercom);

    public void SetNickname(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || Player == null)
            return;

        Name = name;
        ApplyNickname(Player, name);
    }

    public void StopAudio(bool clearQueue = true)
    {
        if (_graceHandle.IsRunning)
            Timing.KillCoroutines(_graceHandle);

        _graceScheduled = false;
        if (Player?.ReferenceHub != null)
            GameIntercom.TrySetOverride(Player.ReferenceHub, false);

        try
        {
            _streamer?.Stop(clearQueue);
        }
        catch (Exception ex)
        {
            Log.Error($"StopAudio failed for bot '{Name}' (ID={ID}): {ex}");
        }
    }

    internal void Duck(float duckVolume, float fadeTime) => _streamer?.Duck(duckVolume, fadeTime);

    internal void Unduck(float fadeTime) => _streamer?.Unduck(fadeTime);

    public void HandleExternalNpcDestroy()
    {
        StopFollowing();

        if (_streamer != null)
        {
            _streamer.Stop(clearQueue: true);
            Object.Destroy(_streamer.gameObject);
            _streamer = null;
        }

        Plugin.AudioPlayerList.TryRemove(ID, out _);
        ControllerIdPool.Release(ID);
        Player = null;
        Log.Debug($"Bot '{Name}' (ID={ID}): NPC destroyed externally.");
    }

    public void SafeDestroy()
    {
        StopFollowing();
        Plugin.AudioPlayerList.TryRemove(ID, out _);
        if (Player?.ReferenceHub != null)
            GameIntercom.TrySetOverride(Player.ReferenceHub, false);

        StopAudio(clearQueue: true);

        if (Player is Npc npc)
            try { if (npc.IsConnected) npc.Destroy(); } catch { }

        if (_streamer != null)
        {
            Object.Destroy(_streamer.gameObject);
            _streamer = null;
        }

        ControllerIdPool.Release(ID);
        Player = null;

        Log.Debug($"Bot '{Name}' (ID={ID}): destroyed.");
    }

    private bool ShouldDelayForGrace(
        string resolvedPath,
        float volume,
        bool loop,
        VoiceChatChannel? channel,
        IEnumerable<int> targetPlayerIds,
        bool shuffle,
        bool continueQueue,
        TimeSpan? startAt,
        TimeSpan? endAt)
    {
        float graceDelay = Plugin.Instance?.Config?.RoundStartGraceDelay ?? 0f;
        if (graceDelay <= 0f || Plugin.RoundStartTime == DateTime.MinValue)
            return false;

        float elapsed = (float)(DateTime.UtcNow - Plugin.RoundStartTime).TotalSeconds;
        float remaining = graceDelay - elapsed;
        if (remaining <= 0f)
            return false;

        if (_graceHandle.IsRunning)
            Timing.KillCoroutines(_graceHandle);

        int[] targets = targetPlayerIds == null ? null : new List<int>(targetPlayerIds).ToArray();
        _graceScheduled = true;
        Log.Debug($"Grace-delay {remaining:F2}s before '{Path.GetFileName(resolvedPath)}'.");

        _graceHandle = Timing.CallDelayed(remaining, () =>
        {
            _graceScheduled = false;
            PlayFile(resolvedPath, volume, loop, channel, targets, shuffle, continueQueue, startAt, endAt);
        });

        return _graceScheduled;
    }

    private IEnumerator<float> FollowCoroutine(Player target, float interval)
    {
        while (_streamer != null && Player != null && target != null && target.ReferenceHub != null && target.IsConnected)
        {
            try
            {
                Player.Position = target.Position;
            }
            catch (Exception ex)
            {
                Log.Error($"FollowPlayer failed for bot '{Name}' (ID={ID}): {ex.Message}");
                yield break;
            }

            yield return Timing.WaitForSeconds(interval);
        }
    }

    private static void ApplyNickname(Player player, string name)
    {
        if (player?.ReferenceHub?.nicknameSync == null)
            return;

        player.ReferenceHub.nicknameSync.Network_myNickSync = name;
        try
        {
            if (Player.Get(player.ReferenceHub) != null)
                player.ReferenceHub.nicknameSync.DisplayName = name;
        }
        catch (Exception ex)
        {
            Log.Debug($"ApplyNickname DisplayName skipped: {ex.Message}");
        }
    }
}
