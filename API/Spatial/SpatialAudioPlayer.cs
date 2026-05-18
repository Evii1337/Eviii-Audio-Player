using AdminToys;
using EviAudio.API;
using Exiled.API.Features;
using MEC;
using Mirror;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using VoiceChat.Codec;
using VoiceChat.Codec.Enums;
using VoiceChat.Networking;

namespace EviAudio.API.Spatial;

public sealed class SpatialAudioPlayer : MonoBehaviour
{
    private const int DefaultMaxPacketsPerUpdate = 5;

    private static SpeakerToy _cachedPrefab;

    private readonly Queue<QueueEntry> _queue = new();
    private readonly object _queueLock = new();
    private readonly object _playbackLock = new();
    private readonly List<ReferenceHub> _frameHubs = new();
    private readonly float[] _pcmBuffer = new float[AudioClipPlayback.PacketSize];
    private readonly float[] _personalPcmBuffer = new float[AudioClipPlayback.PacketSize];
    private readonly byte[] _encodedBuffer = new byte[1276];
    private readonly byte[] _personalEncodedBuffer = new byte[1276];
    private readonly Dictionary<int, OpusEncoder> _personalEncoders = new();
    private float[] _samples = Array.Empty<float>();
    private float[] _crossfadeSamples;
    private int _crossfadeOffset;
    private int _crossfadeLength;
    private int _sampleOffset;
    private int _startSampleOffset;
    private int _endSampleOffset = -1;
    private double _lastSendTime = -1;
    private volatile PendingPlay _pendingPlay;
    private volatile int _loadVersion;
    private OpusEncoder _encoder;
    private byte _allocatedId;
    private float _lifetimeRemaining;
    private bool _lifetimeActive;
    private CoroutineHandle _fadeHandle;
    private Transform _attachedTo;
    private int _registryId;
    private AudioPcmStream _stream;
    private bool _streamErrorLogged;
    private TimeSpan? _pendingSeek;

    public SpeakerToy Speaker { get; private set; }
    public float Volume { get; set; } = 1f;
    public bool Loop { get; set; }
    public float PitchShift { get; set; } = 0f;
    public float Lifetime { get; set; } = 0f;
    public bool IsPlaying => _samples.Length > 0 || _pendingPlay != null || _stream != null;
    public string CurrentFile { get; private set; } = string.Empty;
    public int RegistryId => _registryId;
    public HashSet<int> BroadcastTo { get; } = new();
    public Dictionary<int, float> PlayerVolumes { get; } = new();
    public Func<ReferenceHub, bool> Condition { get; set; }
    public AudioTrackMetadata CurrentMetadata { get; private set; } = new();
    public TimeSpan Position => TimeSpan.FromSeconds((double)_sampleOffset / AudioClipPlayback.SamplingRate);
    public TimeSpan Duration => CurrentMetadata?.Duration ?? TimeSpan.Zero;
    public float CrossfadeSeconds { get; set; } = 0.35f;

    public event Action OnTrackFinished;

    public static SpatialAudioPlayer Create(
        Vector3 position,
        float volume = 1f,
        bool isSpatial = true,
        float minDistance = 5f,
        float maxDistance = 15f)
    {
        SpeakerToy prefab = GetPrefab();
        if (prefab == null)
        {
            Log.Error("SpatialAudioPlayer: SpeakerToy prefab not found.");
            return null;
        }

        byte id = ControllerIdPool.Allocate("speaker");
        var toy = UnityEngine.Object.Instantiate(prefab, position, Quaternion.identity);
        toy.NetworkControllerId = id;
        toy.NetworkVolume = volume;
        toy.NetworkIsSpatial = isSpatial;
        toy.NetworkMinDistance = minDistance;
        toy.NetworkMaxDistance = maxDistance;
        NetworkServer.Spawn(toy.gameObject);

        var player = toy.gameObject.AddComponent<SpatialAudioPlayer>();
        player.Speaker = toy;
        player._allocatedId = id;
        player._encoder = new OpusEncoder(OpusApplicationType.Audio);
        player._registryId = SpatialAudioRegistry.Register(player);
        player.Volume = volume;
        return player;
    }

    public void Play(string path, float volume = 1f, bool loop = false, float lifetime = 0f, TimeSpan? startAt = null, TimeSpan? endAt = null)
    {
        TimeSpan start = startAt ?? TimeSpan.Zero;

        lock (_playbackLock)
        {
            _samples = Array.Empty<float>();
            _stream?.Dispose();
            _stream = null;
            _sampleOffset = 0;
            _startSampleOffset = AudioMath.Clamp((int)(start.TotalSeconds * AudioClipPlayback.SamplingRate), 0, int.MaxValue);
            _endSampleOffset = endAt.HasValue
                ? AudioMath.Clamp((int)(endAt.Value.TotalSeconds * AudioClipPlayback.SamplingRate), 0, int.MaxValue)
                : -1;
            Volume = volume;
            Loop = loop;
            CurrentFile = path;
        }

        if (lifetime > 0f)
            Lifetime = lifetime;

        if (Lifetime > 0f)
        {
            _lifetimeRemaining = Lifetime;
            _lifetimeActive = true;
        }

        int version = Interlocked.Increment(ref _loadVersion);
        float pitch = PitchShift;

        Task.Run(() =>
        {
            PendingPlay pending = null;

            try
            {
                if (_loadVersion != version)
                    return;

                pending = CreatePendingPlay(path, pitch, start, endAt, version);
                pending.Volume = volume;
                pending.Loop = loop;
            }
            catch (OperationCanceledException)
            {
                pending = null;
            }
            catch (Exception ex)
            {
                Log.Error($"SpatialAudioPlayer: failed to decode '{path}': {ex.Message}");
            }

            if (_loadVersion == version)
                _pendingPlay = pending;
            else
                pending?.Dispose();
        });
    }

    public void Enqueue(string path, float volume = 1f, bool loop = false)
    {
        lock (_queueLock)
            _queue.Enqueue(new QueueEntry(path, volume, loop));
    }

    public List<string> GetQueue()
    {
        lock (_queueLock)
        {
            var list = new List<string>();

            foreach (var entry in _queue)
                list.Add(entry.Path);

            return list;
        }
    }

    public void Stop()
    {
        Interlocked.Increment(ref _loadVersion);
        _pendingPlay?.Dispose();
        _pendingPlay = null;

        lock (_playbackLock)
        {
            _stream?.Dispose();
            _stream = null;
            _samples = Array.Empty<float>();
            _crossfadeSamples = null;
            _sampleOffset = 0;
            _startSampleOffset = 0;
            _endSampleOffset = -1;
            _pendingSeek = null;
            _lastSendTime = -1;
            _lifetimeActive = false;
            CurrentFile = string.Empty;
            CurrentMetadata = new AudioTrackMetadata();
            _streamErrorLogged = false;
        }

        lock (_queueLock) _queue.Clear();
    }

    public bool SeekTo(TimeSpan position)
    {
        if (position < TimeSpan.Zero)
            position = TimeSpan.Zero;

        lock (_playbackLock)
        {
            int target = AudioMath.Clamp((int)(position.TotalSeconds * AudioClipPlayback.SamplingRate), 0, int.MaxValue);

            if (_stream != null)
            {
                try
                {
                    var newStream = PcmDecoder.OpenStream(CurrentFile, position);
                    _stream.Dispose();
                    _stream = newStream;
                    _sampleOffset = target;
                    _startSampleOffset = target;
                    _lastSendTime = -1;
                    _streamErrorLogged = false;
                    return true;
                }
                catch (Exception ex)
                {
                    Log.Warn($"SpatialAudioPlayer: stream seek failed for '{CurrentFile}': {ex.Message}");
                    return false;
                }
            }

            if (_samples.Length == 0)
            {
                if (CurrentFile.Length == 0)
                    return false;

                _pendingSeek = position;
                return true;
            }

            target = AudioMath.Clamp(target, 0, Math.Max(0, _samples.Length - 1));
            _sampleOffset = target;
            _startSampleOffset = target;
            _lastSendTime = -1;
            return true;
        }
    }

    public void CrossfadeTo(string path, float volume = 1f, bool loop = false)
    {
        int version = Interlocked.Increment(ref _loadVersion);
        float pitch = PitchShift;
        float[] previous;
        int previousOffset;

        lock (_playbackLock)
        {
            previous = _stream == null ? _samples : Array.Empty<float>();
            previousOffset = _sampleOffset;
        }

        int fadeSamples = Math.Max(AudioClipPlayback.PacketSize, (int)(CrossfadeSeconds * AudioClipPlayback.SamplingRate));

        Task.Run(() =>
        {
            PendingPlay pending = null;

            try
            {
                pending = CreatePendingPlay(path, pitch, TimeSpan.Zero, null, version);
                pending.Volume = volume;
                pending.Loop = loop;
                pending.CrossfadeSamples = previous;
                pending.CrossfadeOffset = previousOffset;
                pending.CrossfadeLength = fadeSamples;
            }
            catch (OperationCanceledException)
            {
                pending = null;
            }
            catch (Exception ex)
            {
                Log.Error($"SpatialAudioPlayer: failed to crossfade '{path}': {ex.Message}");
            }

            if (_loadVersion == version)
                _pendingPlay = pending;
            else
                pending?.Dispose();
        });
    }

    public void SetPlayerVolume(int playerId, float volume)
    {
        volume = AudioMath.Clamp01(volume);

        if (Math.Abs(volume - 1f) < 0.001f)
        {
            PlayerVolumes.Remove(playerId);
            DisposePersonalEncoder(playerId);
        }
        else
        {
            PlayerVolumes[playerId] = volume;
        }
    }

    public void RemovePlayerData(int playerId)
    {
        PlayerVolumes.Remove(playerId);
        BroadcastTo.Remove(playerId);
        DisposePersonalEncoder(playerId);
    }

    public int GetListenerCount()
    {
        int count = 0;

        foreach (ReferenceHub hub in ReferenceHub.AllHubs)
            if (CanSendTo(hub))
                count++;

        return count;
    }

    public void AttachTo(Transform target) => _attachedTo = target;

    public void DetachFrom() => _attachedTo = null;

    public void FadeTo(float targetVolume, float duration)
    {
        if (_fadeHandle.IsRunning)
            Timing.KillCoroutines(_fadeHandle);

        _fadeHandle = Timing.RunCoroutine(FadeCoroutine(Volume, targetVolume, duration));
    }

    public void SetPosition(Vector3 position)
    {
        if (Speaker != null)
            Speaker.transform.position = position;
    }

    public void SetRotation(Quaternion rotation)
    {
        if (Speaker != null)
            Speaker.transform.rotation = rotation;
    }

    public void SetMinDistance(float min)
    {
        if (Speaker != null)
            Speaker.NetworkMinDistance = min;
    }

    public void SetMaxDistance(float max)
    {
        if (Speaker != null)
            Speaker.NetworkMaxDistance = max;
    }

    public void SetSpatial(bool spatial)
    {
        if (Speaker != null)
            Speaker.NetworkIsSpatial = spatial;
    }

    public void DestroySelf()
    {
        if (Speaker != null && Speaker.gameObject != null)
            NetworkServer.Destroy(Speaker.gameObject);
        else
            UnityEngine.Object.Destroy(gameObject);
    }

    private IEnumerator<float> FadeCoroutine(float from, float to, float duration)
    {
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            Volume = Mathf.Lerp(from, to, elapsed / duration);
            if (Speaker != null) Speaker.NetworkVolume = Volume;
            yield return Timing.WaitForOneFrame;
        }

        Volume = to;
        if (Speaker != null) Speaker.NetworkVolume = Volume;
    }

    private void Update()
    {
        if (_attachedTo != null && Speaker != null)
            Speaker.transform.position = _attachedTo.position;

        var pending = _pendingPlay;
        if (pending != null)
        {
            _pendingPlay = null;
            ApplyPending(pending);
        }

        if (_samples.Length == 0 && _stream == null) return;

        if (_lifetimeActive)
        {
            _lifetimeRemaining -= Time.deltaTime;
            if (_lifetimeRemaining <= 0f)
            {
                DestroySelf();
                return;
            }
        }

        double now = Time.unscaledTimeAsDouble;
        double interval = (double)AudioClipPlayback.PacketSize / AudioClipPlayback.SamplingRate;

        if (_lastSendTime < 0)
            _lastSendTime = now;

        _frameHubs.Clear();
        foreach (ReferenceHub hub in ReferenceHub.AllHubs)
            _frameHubs.Add(hub);

        int sent = 0;
        int maxPackets = Math.Max(1, Plugin.Instance?.Config?.MaxAudioPacketsPerFrame ?? DefaultMaxPacketsPerUpdate);

        while (now - _lastSendTime >= interval && sent < maxPackets)
        {
            if (!SendPacket()) break;
            _lastSendTime += interval;
            sent++;
        }

        if (sent >= maxPackets && now - _lastSendTime >= interval)
            _lastSendTime = now - interval;
    }

    private void ApplyPending(PendingPlay pending)
    {
        lock (_playbackLock)
        {
            _stream?.Dispose();
            _stream = pending.Stream;
            _samples = pending.Samples ?? Array.Empty<float>();
            CurrentMetadata = pending.Metadata ?? new AudioTrackMetadata();
            CurrentFile = pending.Path;
            Volume = pending.Volume;
            if (Speaker != null) Speaker.NetworkVolume = Volume;
            Loop = pending.Loop;
            _crossfadeSamples = pending.CrossfadeSamples;
            _crossfadeOffset = pending.CrossfadeOffset;
            _crossfadeLength = pending.CrossfadeLength;

            TimeSpan startAt = _pendingSeek ?? pending.StartAt;
            _pendingSeek = null;
            _startSampleOffset = AudioMath.Clamp((int)(startAt.TotalSeconds * AudioClipPlayback.SamplingRate), 0, int.MaxValue);
            _endSampleOffset = pending.EndAt.HasValue
                ? AudioMath.Clamp((int)(pending.EndAt.Value.TotalSeconds * AudioClipPlayback.SamplingRate), 0, int.MaxValue)
                : -1;

            if (_samples.Length > 0)
            {
                _startSampleOffset = AudioMath.Clamp(_startSampleOffset, 0, Math.Max(0, _samples.Length - 1));
                if (_endSampleOffset >= 0)
                    _endSampleOffset = AudioMath.Clamp(_endSampleOffset, _startSampleOffset + 1, _samples.Length);
            }

            _sampleOffset = _startSampleOffset;
            _lastSendTime = -1;
            _streamErrorLogged = false;
        }
    }

    private bool SendPacket()
    {
        if (_stream != null)
            return SendStreamPacket();

        if (_sampleOffset >= _samples.Length || (_endSampleOffset >= 0 && _sampleOffset >= _endSampleOffset))
            return FinishOrLoop();

        int remaining = _samples.Length - _sampleOffset;
        if (_endSampleOffset >= 0)
            remaining = Math.Min(remaining, _endSampleOffset - _sampleOffset);

        int count = Math.Min(AudioClipPlayback.PacketSize, remaining);
        Array.Copy(_samples, _sampleOffset, _pcmBuffer, 0, count);

        if (count < AudioClipPlayback.PacketSize)
            Array.Clear(_pcmBuffer, count, AudioClipPlayback.PacketSize - count);

        ApplyCrossfade(count);
        ApplyVolumeAndLimit(_pcmBuffer, Volume);
        _sampleOffset += count;
        SendEncodedFrame();
        return true;
    }

    private bool SendStreamPacket()
    {
        Exception error = _stream.ConsumeError();
        if (error != null && !_streamErrorLogged)
        {
            _streamErrorLogged = true;
            Log.Error($"SpatialAudioPlayer: stream '{CurrentFile}' failed: {error.Message}");
        }

        if (_endSampleOffset >= 0 && _sampleOffset >= _endSampleOffset)
            return FinishOrLoop();

        int requested = AudioClipPlayback.PacketSize;
        if (_endSampleOffset >= 0)
            requested = Math.Min(requested, Math.Max(0, _endSampleOffset - _sampleOffset));

        int count = requested > 0 ? _stream.Read(_pcmBuffer, 0, requested) : 0;

        if (count == 0 && _stream.IsCompleted)
            return FinishOrLoop();

        if (count < AudioClipPlayback.PacketSize)
            Array.Clear(_pcmBuffer, count, AudioClipPlayback.PacketSize - count);

        ApplyVolumeAndLimit(_pcmBuffer, Volume);
        _sampleOffset += count;
        SendEncodedFrame();
        return true;
    }

    private void ApplyCrossfade(int count)
    {
        if (_crossfadeSamples == null || _crossfadeLength <= 0)
            return;

        for (int i = 0; i < AudioClipPlayback.PacketSize; i++)
        {
            int oldIndex = _crossfadeOffset + i;
            if (oldIndex >= _crossfadeSamples.Length)
            {
                _crossfadeSamples = null;
                return;
            }

            float t = AudioMath.Clamp01((float)(_sampleOffset + i) / _crossfadeLength);
            _pcmBuffer[i] = _pcmBuffer[i] * t + _crossfadeSamples[oldIndex] * (1f - t);
        }

        _crossfadeOffset += count;

        if (_sampleOffset + count >= _crossfadeLength)
            _crossfadeSamples = null;
    }

    private bool FinishOrLoop()
    {
        if (Loop)
        {
            if (_stream != null)
            {
                try
                {
                    _stream.Dispose();
                    _stream = PcmDecoder.OpenStream(CurrentFile, TimeSpan.FromSeconds((double)_startSampleOffset / AudioClipPlayback.SamplingRate));
                    _sampleOffset = _startSampleOffset;
                    _streamErrorLogged = false;
                    return true;
                }
                catch (Exception ex)
                {
                    Log.Warn($"SpatialAudioPlayer: failed to restart stream loop '{CurrentFile}': {ex.Message}");
                }
            }
            else
            {
                _sampleOffset = _startSampleOffset;
                return true;
            }
        }

        bool hasNext;
        QueueEntry next = default;
        lock (_queueLock)
        {
            hasNext = _queue.Count > 0;
            if (hasNext) next = _queue.Dequeue();
        }

        if (hasNext)
        {
            Play(next.Path, next.Volume, next.Loop);
            OnTrackFinished?.Invoke();
        }
        else
        {
            CurrentFile = string.Empty;
            CurrentMetadata = new AudioTrackMetadata();
            _stream?.Dispose();
            _stream = null;
            _samples = Array.Empty<float>();
            _sampleOffset = 0;
            _startSampleOffset = 0;
            _endSampleOffset = -1;
            _lastSendTime = -1;
            OnTrackFinished?.Invoke();
        }

        return false;
    }

    private void ApplyVolumeAndLimit(float[] buffer, float volume)
    {
        for (int i = 0; i < AudioClipPlayback.PacketSize; i++)
            buffer[i] = AudioMath.SoftLimit(buffer[i] * volume);
    }

    private void SendEncodedFrame()
    {
        if (Speaker == null)
            return;

        int encodedLen = _encoder.Encode(_pcmBuffer, _encodedBuffer);
        if (encodedLen <= 0) return;

        var msg = new AudioMessage(Speaker.ControllerId, _encodedBuffer, encodedLen);
        IEnumerable<ReferenceHub> hubs = _frameHubs.Count > 0 ? _frameHubs : ReferenceHub.AllHubs;

        foreach (ReferenceHub hub in hubs)
        {
            if (!CanSendTo(hub)) continue;

            if (PlayerVolumes.TryGetValue(hub.PlayerId, out float personalVolume))
            {
                Array.Copy(_pcmBuffer, _personalPcmBuffer, AudioClipPlayback.PacketSize);
                ApplyVolumeAndLimit(_personalPcmBuffer, personalVolume);
                int personalLen = GetPersonalEncoder(hub.PlayerId).Encode(_personalPcmBuffer, _personalEncodedBuffer);
                if (personalLen > 0)
                    hub.connectionToClient.Send(new AudioMessage(Speaker.ControllerId, _personalEncodedBuffer, personalLen));
            }
            else
            {
                hub.connectionToClient.Send(msg);
            }
        }
    }

    private bool CanSendTo(ReferenceHub hub)
    {
        if (hub?.connectionToClient == null)
            return false;

        if (BroadcastTo.Count > 0 && !BroadcastTo.Contains(hub.PlayerId))
            return false;

        if (Condition != null && !Condition(hub))
            return false;

        return true;
    }

    private OpusEncoder GetPersonalEncoder(int playerId)
    {
        if (_personalEncoders.TryGetValue(playerId, out var encoder))
            return encoder;

        encoder = new OpusEncoder(OpusApplicationType.Audio);
        _personalEncoders[playerId] = encoder;
        return encoder;
    }

    private void DisposePersonalEncoder(int playerId)
    {
        if (!_personalEncoders.TryGetValue(playerId, out var encoder))
            return;

        _personalEncoders.Remove(playerId);
        try { encoder.Dispose(); } catch { }
    }

    private PendingPlay CreatePendingPlay(string path, float pitch, TimeSpan startAt, TimeSpan? endAt, int version)
    {
        if (PcmDecoder.ShouldStream(path))
            return new PendingPlay(Array.Empty<float>(), PcmDecoder.OpenStream(path, startAt, () => _loadVersion == version), new AudioTrackMetadata(), path, startAt, endAt);

        AudioClipData data = AudioClipCache.GetOrDecode(path, pitch);
        return new PendingPlay(data.Samples, null, data.Metadata, path, startAt, endAt);
    }

    private void OnDestroy()
    {
        Interlocked.Increment(ref _loadVersion);
        _pendingPlay?.Dispose();
        _pendingPlay = null;
        _stream?.Dispose();
        _stream = null;
        _encoder?.Dispose();
        _encoder = null;

        foreach (var encoder in _personalEncoders.Values)
            encoder.Dispose();

        _personalEncoders.Clear();

        ControllerIdPool.Release(_allocatedId);
        SpatialAudioRegistry.Unregister(_registryId);

        if (_fadeHandle.IsRunning)
            Timing.KillCoroutines(_fadeHandle);

        Speaker = null;
    }

    private static SpeakerToy GetPrefab()
    {
        if (_cachedPrefab != null)
            return _cachedPrefab;

        foreach (var pref in NetworkClient.prefabs.Values)
        {
            if (pref.TryGetComponent(out SpeakerToy prefab))
            {
                _cachedPrefab = prefab;
                return prefab;
            }
        }

        return null;
    }

    private sealed class PendingPlay : IDisposable
    {
        public readonly float[] Samples;
        public readonly AudioPcmStream Stream;
        public readonly AudioTrackMetadata Metadata;
        public readonly string Path;
        public readonly TimeSpan StartAt;
        public readonly TimeSpan? EndAt;
        public float Volume;
        public bool Loop;
        public float[] CrossfadeSamples;
        public int CrossfadeOffset;
        public int CrossfadeLength;

        public PendingPlay(float[] samples, AudioPcmStream stream, AudioTrackMetadata metadata, string path, TimeSpan startAt, TimeSpan? endAt)
        {
            Samples = samples;
            Stream = stream;
            Metadata = metadata;
            Path = path;
            StartAt = startAt;
            EndAt = endAt;
        }

        public void Dispose() => Stream?.Dispose();
    }

    private readonly struct QueueEntry
    {
        public readonly string Path;
        public readonly float Volume;
        public readonly bool Loop;

        public QueueEntry(string path, float volume, bool loop)
        {
            Path = path;
            Volume = volume;
            Loop = loop;
        }
    }
}
