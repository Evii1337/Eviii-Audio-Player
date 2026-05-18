using EviAudio.API.Container;
using EviAudio.API.Preset;
using EviAudio.API.Spatial;
using Exiled.API.Features;
using PlayerRoles;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VoiceChat;

namespace EviAudio.API;

public static class EviAudioAPI
{
    public static bool IsReady => Plugin.Instance != null;
    public static int DefaultBotId => Plugin.Instance?.Config?.DefaultBotId ?? 99;
    public static string TracksPath => Plugin.Instance?.AudioPath ?? string.Empty;

    private static bool EnsureReady(out string error)
    {
        if (Plugin.Instance == null)
        {
            error = "EviAudio plugin is not loaded.";
            return false;
        }

        error = null;
        return true;
    }

    private static void LogError(string message) => Log.Error(message);
    private static void LogWarn(string message) => Log.Warn(message);

    private static int[] NormalizePlayerIds(IEnumerable<int> ids)
        => ids?.Where(id => id > 0).Distinct().ToArray();

    private static int[] PlayerIdsFromPlayers(IEnumerable<Player> players)
        => players?
            .Where(player => player != null && player.IsConnected && !player.IsAudioPlayer())
            .Select(player => player.Id)
            .Distinct()
            .ToArray();

    public static AudioPlayerBot SpawnBot(
        string name = "[BOT] EviAudio",
        int id = 99,
        RoleTypeId role = RoleTypeId.Tutorial,
        bool ignored = true)
    {
        if (!EnsureReady(out string err)) { LogError(err); return null; }
        return AudioController.SpawnDummy(id, name, role, ignored);
    }

    public static AudioPlayerBot GetOrSpawnBot(int id = 99, string name = null)
    {
        if (!EnsureReady(out string err)) { LogError(err); return null; }

        var existing = AudioController.TryGetAudioPlayerContainer(id);
        if (existing != null)
            return existing;

        if (!string.IsNullOrWhiteSpace(name))
            return AudioController.SpawnDummy(id, name);

        return AudioController.GetOrSpawnAudioPlayerContainer(id);
    }

    public static bool TryGetBot(int id, out AudioPlayerBot bot)
    {
        bot = null;
        if (!EnsureReady(out _)) return false;
        bot = AudioController.TryGetAudioPlayerContainer(id);
        return bot != null;
    }

    public static AudioPlayerBot GetBot(int id)
    {
        TryGetBot(id, out var bot);
        return bot;
    }

    public static IReadOnlyCollection<AudioPlayerBot> GetAllBots()
    {
        if (!EnsureReady(out _)) return Array.Empty<AudioPlayerBot>();
        return AudioController.GetAllAudioPlayers().ToList();
    }

    public static IReadOnlyCollection<int> GetOnlinePlayerIds(bool includeAudioBots = false)
    {
        if (!EnsureReady(out _)) return Array.Empty<int>();

        return Player.List
            .Where(player => player != null && player.IsConnected && (includeAudioBots || !player.IsAudioPlayer()))
            .Select(player => player.Id)
            .Distinct()
            .ToList();
    }

    public static IReadOnlyCollection<int> GetRolePlayerIds(RoleTypeId role, bool includeAudioBots = false)
    {
        if (!EnsureReady(out _)) return Array.Empty<int>();

        return Player.List
            .Where(player => player != null && player.IsConnected && player.Role.Type == role && (includeAudioBots || !player.IsAudioPlayer()))
            .Select(player => player.Id)
            .Distinct()
            .ToList();
    }

    public static bool TryPlay(
        int botId,
        string filePath,
        float volume = 100f,
        bool loop = false,
        VoiceChatChannel? channel = null,
        IEnumerable<int> targetPlayerIds = null,
        TimeSpan? startAt = null,
        TimeSpan? endAt = null,
        bool spawnBot = false)
    {
        if (!EnsureReady(out string err)) { LogError(err); return false; }

        var bot = spawnBot
            ? AudioController.GetOrSpawnAudioPlayerContainer(botId)
            : AudioController.TryGetAudioPlayerContainer(botId);

        if (bot == null)
        {
            LogWarn($"Bot {botId} not found.");
            return false;
        }

        bot.PlayFile(filePath, volume, loop, channel, NormalizePlayerIds(targetPlayerIds), startAt: startAt, endAt: endAt);
        return true;
    }

    public static void Play(
        int botId,
        string filePath,
        float volume = 100f,
        bool loop = false,
        VoiceChatChannel? channel = null,
        IEnumerable<int> targetPlayerIds = null,
        TimeSpan? startAt = null,
        TimeSpan? endAt = null)
        => TryPlay(botId, filePath, volume, loop, channel, targetPlayerIds, startAt, endAt);

    public static bool TryPlayToAll(
        string filePath,
        float volume = 100f,
        bool loop = false,
        IEnumerable<int> targets = null,
        int botId = 99,
        VoiceChatChannel channel = VoiceChatChannel.Intercom)
    {
        IEnumerable<int> targetIds = targets ?? GetOnlinePlayerIds();
        return TryPlay(botId, filePath, volume, loop, channel, targetIds, spawnBot: true);
    }

    public static void PlayToAll(
        string filePath,
        float volume = 100f,
        bool loop = false,
        IEnumerable<int> targets = null,
        int botId = 99)
        => TryPlayToAll(filePath, volume, loop, targets, botId);

    public static bool TryPlayToPlayers(
        IEnumerable<Player> players,
        string filePath,
        float volume = 100f,
        int botId = 99,
        bool loop = false,
        VoiceChatChannel channel = VoiceChatChannel.Intercom)
    {
        int[] ids = PlayerIdsFromPlayers(players);
        if (ids == null || ids.Length == 0)
        {
            LogWarn("TryPlayToPlayers called with no online players.");
            return false;
        }

        return TryPlayToAll(filePath, volume, loop, ids, botId, channel);
    }

    public static bool TryPlayToPlayer(
        Player player,
        string filePath,
        float volume = 100f,
        int botId = 99,
        bool loop = false,
        VoiceChatChannel channel = VoiceChatChannel.Intercom)
        => TryPlayToPlayers(player == null ? null : new[] { player }, filePath, volume, botId, loop, channel);

    public static bool TryPlayToRole(RoleTypeId role, string filePath, float volume = 100f, int botId = 99, bool loop = false)
        => TryPlayToAll(filePath, volume, loop, GetRolePlayerIds(role), botId);

    public static void PlayToRole(RoleTypeId role, string filePath, float volume = 100f, int botId = 99, bool loop = false)
        => TryPlayToRole(role, filePath, volume, botId, loop);

    public static void PlayToPlayer(Player player, string filePath, float volume = 100f, int botId = 99, bool loop = false)
        => TryPlayToPlayer(player, filePath, volume, botId, loop);

    public static bool TryPlayFolder(
        int botId,
        string folderPath,
        float volume = 100f,
        bool shuffle = false,
        VoiceChatChannel? channel = null,
        IEnumerable<int> targetPlayerIds = null,
        bool spawnBot = false)
    {
        if (!EnsureReady(out string err)) { LogError(err); return false; }

        var bot = spawnBot
            ? AudioController.GetOrSpawnAudioPlayerContainer(botId)
            : AudioController.TryGetAudioPlayerContainer(botId);

        if (bot == null)
        {
            LogWarn($"Bot {botId} not found.");
            return false;
        }

        bot.PlayFolder(folderPath, volume, shuffle, channel, NormalizePlayerIds(targetPlayerIds));
        return true;
    }

    public static void PlayFolder(
        int botId,
        string folderPath,
        float volume = 100f,
        bool shuffle = false,
        VoiceChatChannel? channel = null)
        => TryPlayFolder(botId, folderPath, volume, shuffle, channel);

    public static void PlayFolder(
        int botId,
        string folderPath,
        float volume,
        bool shuffle,
        VoiceChatChannel? channel,
        IEnumerable<int> targetPlayerIds)
        => TryPlayFolder(botId, folderPath, volume, shuffle, channel, targetPlayerIds);

    public static bool TryPlayM3U(
        int botId,
        string m3uPath,
        float volume = 100f,
        VoiceChatChannel? channel = null,
        IEnumerable<int> targetPlayerIds = null,
        bool spawnBot = false)
    {
        if (!EnsureReady(out string err)) { LogError(err); return false; }

        var bot = spawnBot
            ? AudioController.GetOrSpawnAudioPlayerContainer(botId)
            : AudioController.TryGetAudioPlayerContainer(botId);

        if (bot == null)
        {
            LogWarn($"Bot {botId} not found.");
            return false;
        }

        bot.PlayM3U(m3uPath, volume, channel, NormalizePlayerIds(targetPlayerIds));
        return true;
    }

    public static void PlayM3U(int botId, string m3uPath, float volume = 100f, VoiceChatChannel? channel = null)
        => TryPlayM3U(botId, m3uPath, volume, channel);

    public static bool TryStop(int botId, bool clearQueue = true)
    {
        if (!EnsureReady(out string err)) { LogError(err); return false; }

        var bot = AudioController.TryGetAudioPlayerContainer(botId);
        if (bot == null)
            return false;

        bot.StopAudio(clearQueue);
        return true;
    }

    public static void Stop(int botId, bool clearQueue = true)
        => TryStop(botId, clearQueue);

    public static int TryStopAll(bool clearQueue = true)
    {
        if (!EnsureReady(out string err)) { LogError(err); return 0; }

        int count = 0;
        foreach (AudioPlayerBot bot in AudioController.GetAllAudioPlayers().ToList())
        {
            bot.StopAudio(clearQueue);
            count++;
        }

        return count;
    }

    public static void StopAll(bool clearQueue = true)
        => TryStopAll(clearQueue);

    public static bool IsAnyBotPlaying()
    {
        if (!EnsureReady(out _)) return false;
        return AudioController.GetAllAudioPlayers().Any(bot => bot.IsPlaying);
    }

    public static string GetCurrentTrack(int botId)
    {
        if (!EnsureReady(out _)) return string.Empty;
        return AudioController.TryGetAudioPlayerContainer(botId)?.CurrentTrack ?? string.Empty;
    }

    public static int GetListenerCount(int botId)
    {
        if (!EnsureReady(out _)) return 0;
        return AudioController.TryGetAudioPlayerContainer(botId)?.GetListenerCount() ?? 0;
    }

    public static bool TrySetVolume(int botId, float volume)
    {
        if (!TryGetBot(botId, out var bot)) return false;
        bot.Volume = volume;
        return true;
    }

    public static bool TrySetLoop(int botId, bool loop)
    {
        if (!TryGetBot(botId, out var bot)) return false;
        bot.Loop = loop;
        return true;
    }

    public static bool TrySetPaused(int botId, bool paused)
    {
        if (!TryGetBot(botId, out var bot)) return false;
        bot.IsPaused = paused;
        return true;
    }

    public static bool TrySetVoiceChannel(int botId, VoiceChatChannel channel)
    {
        if (!TryGetBot(botId, out var bot)) return false;
        bot.VoiceChatChannel = channel;
        return true;
    }

    public static void Fade(int botId, float targetVolume, float duration)
    {
        if (!EnsureReady(out string err)) { LogError(err); return; }
        AudioController.TryGetAudioPlayerContainer(botId)?.FadeTo(targetVolume, duration);
    }

    public static bool Seek(int botId, TimeSpan position)
    {
        if (!EnsureReady(out string err)) { LogError(err); return false; }
        return AudioController.TryGetAudioPlayerContainer(botId)?.SeekTo(position) ?? false;
    }

    public static void Crossfade(int botId, string filePath, float volume = 100f, bool loop = false)
    {
        if (!EnsureReady(out string err)) { LogError(err); return; }
        AudioController.TryGetAudioPlayerContainer(botId)?.CrossfadeTo(filePath, volume, loop);
    }

    public static bool TrySetPlayerVolume(int botId, int playerId, float volume)
    {
        if (!TryGetBot(botId, out var bot)) return false;
        bot.SetPlayerVolume(playerId, volume);
        return true;
    }

    public static void SetPlayerVolume(int botId, int playerId, float volume)
        => TrySetPlayerVolume(botId, playerId, volume);

    public static bool TryClearPlayerVolume(int botId, int playerId)
        => TrySetPlayerVolume(botId, playerId, 1f);

    public static bool TrySetTargets(int botId, IEnumerable<int> targetPlayerIds)
    {
        if (!TryGetBot(botId, out var bot) || bot.BroadcastTo == null)
            return false;

        bot.BroadcastTo.Clear();
        int[] ids = NormalizePlayerIds(targetPlayerIds);
        if (ids != null)
            foreach (int id in ids)
                bot.BroadcastTo.Add(id);

        return true;
    }

    public static bool TryClearTargets(int botId)
    {
        if (!TryGetBot(botId, out var bot) || bot.BroadcastTo == null)
            return false;

        bot.BroadcastTo.Clear();
        return true;
    }

    public static bool TryEnqueue(int botId, string filePath, int position = -1)
    {
        if (!TryGetBot(botId, out var bot)) return false;
        bot.Enqueue(filePath, position);
        return true;
    }

    public static bool TryInsertNext(int botId, string filePath)
    {
        if (!TryGetBot(botId, out var bot)) return false;
        bot.InsertNext(filePath);
        return true;
    }

    public static bool TryClearQueue(int botId)
    {
        if (!TryGetBot(botId, out var bot)) return false;
        bot.ClearQueue();
        return true;
    }

    public static void FollowPlayer(int botId, Player player, float interval = 0.1f)
    {
        if (!EnsureReady(out string err)) { LogError(err); return; }
        AudioController.TryGetAudioPlayerContainer(botId)?.FollowPlayer(player, interval);
    }

    public static void StopFollowing(int botId)
    {
        if (!EnsureReady(out string err)) { LogError(err); return; }
        AudioController.TryGetAudioPlayerContainer(botId)?.StopFollowing();
    }

    public static SpatialAudioPlayer CreateSpatialPlayer(
        Vector3 position,
        float volume = 1f,
        bool isSpatial = true,
        float minDistance = 5f,
        float maxDistance = 15f,
        float lifetime = 0f,
        float pitchShift = 0f)
    {
        if (!EnsureReady(out string err)) { LogError(err); return null; }
        var player = SpatialAudioPlayer.Create(position, volume, isSpatial, minDistance, maxDistance);
        if (player == null) return null;
        player.Lifetime = lifetime;
        player.PitchShift = pitchShift;
        return player;
    }

    public static SpatialAudioPlayer CreateSpatialPlayer(
        Player player,
        float volume = 1f,
        bool isSpatial = true,
        float minDistance = 5f,
        float maxDistance = 15f,
        float lifetime = 0f,
        float pitchShift = 0f)
    {
        if (player == null)
        {
            LogWarn("CreateSpatialPlayer called with a null player.");
            return null;
        }

        return CreateSpatialPlayer(player.Position, volume, isSpatial, minDistance, maxDistance, lifetime, pitchShift);
    }

    public static (bool success, string message, List<SpatialAudioPlayer> players) ActivateScene(string presetName)
    {
        if (!EnsureReady(out string err)) return (false, err, null);
        return SceneManager.ActivateScene(presetName);
    }

    public static (bool success, string message) DeactivateScene(string presetName)
    {
        if (!EnsureReady(out string err)) return (false, err);
        return SceneManager.DeactivateScene(presetName);
    }

    public static void DeactivateAllScenes()
    {
        if (!EnsureReady(out _)) return;
        SceneManager.DeactivateAll();
    }

    public static SpatialAudioPlayer GetSpatialPlayer(int registryId)
    {
        if (!EnsureReady(out _)) return null;
        return SpatialAudioRegistry.Get(registryId);
    }

    public static IReadOnlyDictionary<int, SpatialAudioPlayer> GetAllSpatialPlayers()
    {
        if (!EnsureReady(out _)) return new Dictionary<int, SpatialAudioPlayer>();
        return SpatialAudioRegistry.All;
    }
}