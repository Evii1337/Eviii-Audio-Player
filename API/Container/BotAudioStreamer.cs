using Exiled.API.Features;
using MEC;
using RelativePositioning;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using VoiceChat;
using VoiceChat.Codec;
using VoiceChat.Codec.Enums;
using VoiceChat.Networking;
using VoiceChat.Playbacks;

namespace EviAudio.API.Container;

public sealed class BotAudioStreamer : MonoBehaviour
{
    private const int DefaultMaxPacketsPerUpdate = 5;

    private readonly List<string> _queue = new();
    private readonly object _queueLock = new();
    private readonly object _playbackLock = new();
    private readonly List<ReferenceHub> _frameHubs = new();
    private readonly float[] _pcmBuffer = new float[AudioClipPlayback.PacketSize];
    private readonly float[] _personalPcmBuffer = new float[AudioClipPlayback.PacketSize];
    private readonly byte[] _encodedBuffer = new byte[1276];
    private readonly byte[] _personalEncodedBuffer = new byte[1276];
    private readonly Dictionary<int, OpusEncoder> _personalEncoders = new();
    private float[] _currentSamples = Array.Empty<float>();
    private float[] _crossfadeSamples;
    private int _crossfadeOffset;
    private int _crossfadeLength;
    private int _sampleOffset;
    private int _startSampleOffset;
    private int _endSampleOffset = -1;
    private double _lastSendTime = -1;
    private double _lastPositionSendTime = -1;
    private volatile PendingLoad _pendingLoad;
    private volatile int _loadVersion;
    private OpusEncoder _encoder;
    private AudioPcmStream _stream;
    private CoroutineHandle _fadeHandle;
    private float _baseDuckVolume;
    private bool _isDucked;
    private float _pitchShift;
    private float _volume = 100f;
    private float _volumeFactor = 1f;
    private bool _streamErrorLogged;
    private TimeSpan? _pendingSeek;

    public ReferenceHub Hub { get; private set; }
    public VoiceChatChannel Channel { get; set; } = VoiceChatChannel.Intercom;
    public float Volume
    {
        get => _volume;
        set
        {
            _volume = AudioMath.Clamp(value, 0f, 100f);
            _volumeFactor = AudioMath.Clamp01(_volume * 0.01f);
        }
    }
    public bool Loop { get; set; }
    public bool ContinueAfterTrack { get; set; }
    public bool Shuffle { get; set; }
    public bool IsPaused { get; set; }
    public float PitchShift { get => _pitchShift; set => _pitchShift = value; }
    public string CurrentPlay { get; private set; } = string.Empty;
    public bool IsPlaying => CurrentPlay.Length > 0;
    public HashSet<int> BroadcastTo { get; } = new();
    public Dictionary<int, float> PlayerVolumes { get; } = new();
    public Func<ReferenceHub, bool> Condition { get; set; }
    public AudioTrackMetadata CurrentMetadata { get; private set; } = new();
    public TimeSpan Position => TimeSpan.FromSeconds((double)_sampleOffset / AudioClipPlayback.SamplingRate);
    public TimeSpan Duration => CurrentMetadata?.Duration ?? TimeSpan.Zero;
    public float CrossfadeSeconds { get; set; } = 0.35f;
    public event Action<string> OnTrackFinished;

    public void Init(ReferenceHub hub)
    {
        Hub = hub;
        _encoder = new OpusEncoder(OpusApplicationType.Audio);
    }

    public void Enqueue(string path, int position = -1)
    {
        lock (_queueLock)
        {
            if (position < 0 || position >= _queue.Count)
            {
                _queue.Add(path);
                return;
            }

            _queue.Insert(position, path);
        }
    }

    public void EnqueueFolder(string folderPath, bool shuffle = false)
    {
        if (!Directory.Exists(folderPath))
        {
            Log.Warn($"EnqueueFolder: folder not found '{folderPath}'.");
            return;
        }

        var supported = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".ogg", ".wav", ".mp3", ".flac", ".aac", ".opus", ".m4a", ".mp4" };

        var files = Directory.GetFiles(folderPath)
            .Where(f => supported.Contains(Path.GetExtension(f)))
            .ToList();

        if (shuffle)
        {
            for (int i = files.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (files[i], files[j]) = (files[j], files[i]);
            }
        }
        else
        {
            files.Sort(StringComparer.OrdinalIgnoreCase);
        }

        lock (_queueLock)
            _queue.AddRange(files);

        Log.Debug($"EnqueueFolder: queued {files.Count} file(s) from '{folderPath}'.");
    }

    public void EnqueueM3U(string m3uPath)
    {
        if (!File.Exists(m3uPath))
        {
            Log.Warn($"EnqueueM3U: file not found '{m3uPath}'.");
            return;
        }

        string dir = Path.GetDirectoryName(m3uPath) ?? string.Empty;
        string[] lines = File.ReadAllLines(m3uPath);
        int count = 0;
        var resolvedFiles = new List<string>();

        foreach (var raw in lines)
        {
            string line = raw.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;

            string resolved = Path.IsPathRooted(line) || PcmDecoder.IsUrl(line) ? line : Path.Combine(dir, line);
            if (PcmDecoder.IsUrl(resolved) || File.Exists(resolved))
            {
                resolvedFiles.Add(resolved);
                count++;
            }
            else
            {
                Log.Warn($"EnqueueM3U: track not found '{resolved}'.");
            }
        }

        lock (_queueLock)
            _queue.AddRange(resolvedFiles);

        Log.Debug($"EnqueueM3U: queued {count} track(s) from '{m3uPath}'.");
    }

    public void Play()
        => Play(TimeSpan.Zero, null);

    public void Play(TimeSpan? startAt, TimeSpan? endAt)
    {
        bool hasItems;
        lock (_queueLock) hasItems = _queue.Count > 0;
        if (hasItems) LoadNextAsync(startAt ?? TimeSpan.Zero, endAt);
    }

    public void Stop(bool clearQueue)
    {
        Interlocked.Increment(ref _loadVersion);
        _pendingLoad?.Dispose();
        _pendingLoad = null;

        lock (_playbackLock)
        {
            _stream?.Dispose();
            _stream = null;
            CurrentPlay = string.Empty;
            CurrentMetadata = new AudioTrackMetadata();
            _currentSamples = Array.Empty<float>();
            _crossfadeSamples = null;
            _crossfadeOffset = 0;
            _crossfadeLength = 0;
            _sampleOffset = 0;
            _startSampleOffset = 0;
            _endSampleOffset = -1;
            _pendingSeek = null;
            _lastSendTime = -1;
            _lastPositionSendTime = -1;
            _streamErrorLogged = false;
            IsPaused = false;
            _isDucked = false;
        }

        if (clearQueue)
            lock (_queueLock) _queue.Clear();
    }

    public void Skip()
    {
        bool hasNext;
        lock (_queueLock) hasNext = _queue.Count > 0;

        string finished = CurrentPlay;
        lock (_playbackLock)
        {
            _stream?.Dispose();
            _stream = null;
            _currentSamples = Array.Empty<float>();
            _crossfadeSamples = null;
            _sampleOffset = 0;
            _startSampleOffset = 0;
            _endSampleOffset = -1;
            _pendingSeek = null;
            _lastSendTime = -1;
            _streamErrorLogged = false;
        }

        if (hasNext)
            LoadNextAsync(TimeSpan.Zero, null);
        else
            CurrentPlay = string.Empty;

        if (!string.IsNullOrEmpty(finished))
            OnTrackFinished?.Invoke(finished);
    }

    public List<string> GetQueue()
    {
        lock (_queueLock)
            return new List<string>(_queue);
    }

    public void ClearQueue()
    {
        lock (_queueLock)
            _queue.Clear();
    }

    public void ShuffleQueue()
    {
        lock (_queueLock)
        {
            for (int i = _queue.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (_queue[i], _queue[j]) = (_queue[j], _queue[i]);
            }
        }
    }

    public bool MoveQueueItem(int from, int to)
    {
        lock (_queueLock)
        {
            if (from < 0 || from >= _queue.Count || to < 0 || to >= _queue.Count)
                return false;

            string item = _queue[from];
            _queue.RemoveAt(from);
            _queue.Insert(to, item);

            return true;
        }
    }

    public bool RemoveQueueAt(int index)
    {
        lock (_queueLock)
        {
            if (index < 0 || index >= _queue.Count)
                return false;

            _queue.RemoveAt(index);
            return true;
        }
    }

    public int RemoveQueueByPath(string path)
    {
        lock (_queueLock)
            return _queue.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase) || string.Equals(Path.GetFileName(p), path, StringComparison.OrdinalIgnoreCase));
    }

    public void InsertNext(string path)
    {
        lock (_queueLock)
            _queue.Insert(0, path);
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
                    var newStream = PcmDecoder.OpenStream(CurrentPlay, position);
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
                    Log.Warn($"BotAudioStreamer: stream seek failed for '{CurrentPlay}': {ex.Message}");
                    return false;
                }
            }

            if (_currentSamples.Length == 0)
            {
                if (CurrentPlay.Length == 0)
                    return false;

                _pendingSeek = position;
                return true;
            }

            target = AudioMath.Clamp(target, 0, Math.Max(0, _currentSamples.Length - 1));
            _sampleOffset = target;
            _startSampleOffset = target;
            _lastSendTime = -1;
            return true;
        }
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

    public void FadeTo(float targetVolume, float duration)
    {
        if (_fadeHandle.IsRunning)
            Timing.KillCoroutines(_fadeHandle);

        _fadeHandle = Timing.RunCoroutine(FadeCoroutine(Volume, targetVolume, duration));
    }

    public void CrossfadeTo(string path, float volume, bool loop)
    {
        int version = Interlocked.Increment(ref _loadVersion);
        float pitch = _pitchShift;
        float[] previous;
        int previousOffset;

        lock (_playbackLock)
        {
            previous = _stream == null ? _currentSamples : Array.Empty<float>();
            previousOffset = _sampleOffset;
        }

        int fadeSamples = Math.Max(AudioClipPlayback.PacketSize, (int)(CrossfadeSeconds * AudioClipPlayback.SamplingRate));

        Task.Run(() =>
        {
            PendingLoad pending = null;

            try
            {
                pending = CreatePendingLoad(path, pitch, TimeSpan.Zero, null, version);
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
                Log.Error($"BotAudioStreamer: failed to crossfade '{path}': {ex.Message}");
            }

            if (_loadVersion == version)
                _pendingLoad = pending;
            else
                pending?.Dispose();
        });
    }

    internal void Duck(float duckVolume, float fadeTime)
    {
        if (_isDucked) return;
        _isDucked = true;
        _baseDuckVolume = Volume;
        FadeTo(duckVolume, fadeTime);
    }

    internal void Unduck(float fadeTime)
    {
        if (!_isDucked) return;
        _isDucked = false;
        FadeTo(_baseDuckVolume, fadeTime);
    }

    public int GetListenerCount()
    {
        int count = 0;

        foreach (ReferenceHub hub in ReferenceHub.AllHubs)
            if (CanSendTo(hub))
                count++;

        return count;
    }

    private IEnumerator<float> FadeCoroutine(float from, float to, float duration)
    {
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            Volume = Mathf.Lerp(from, to, elapsed / duration);
            yield return Timing.WaitForOneFrame;
        }

        Volume = to;
    }

    private void Update()
    {
        var pending = _pendingLoad;
        if (pending != null)
        {
            _pendingLoad = null;
            ApplyPending(pending);
        }

        if (Hub == null || IsPaused || CurrentPlay.Length == 0) return;

        double now = Time.unscaledTimeAsDouble;
        double interval = (double)AudioClipPlayback.PacketSize / AudioClipPlayback.SamplingRate;

        if (_lastSendTime < 0)
            _lastSendTime = now;

        _frameHubs.Clear();
        bool hubsLoaded = false;
        int sent = 0;
        int maxPackets = Math.Max(1, Plugin.Instance?.Config?.MaxAudioPacketsPerFrame ?? DefaultMaxPacketsPerUpdate);

        while (now - _lastSendTime >= interval && sent < maxPackets)
        {
            if (!hubsLoaded)
            {
                foreach (ReferenceHub hub in ReferenceHub.AllHubs)
                    _frameHubs.Add(hub);

                hubsLoaded = true;
            }

            if (!SendPacket()) break;
            _lastSendTime += interval;
            sent++;
        }

        if (sent >= maxPackets && now - _lastSendTime >= interval)
            _lastSendTime = now - interval;
    }

    private void ApplyPending(PendingLoad pending)
    {
        lock (_playbackLock)
        {
            _stream?.Dispose();
            _stream = pending.Stream;
            _currentSamples = pending.Samples ?? Array.Empty<float>();
            CurrentMetadata = pending.Metadata ?? new AudioTrackMetadata();
            CurrentPlay = pending.Path;
            Loop = pending.Loop;
            Volume = pending.Volume;
            _crossfadeSamples = pending.CrossfadeSamples;
            _crossfadeOffset = pending.CrossfadeOffset;
            _crossfadeLength = pending.CrossfadeLength;

            TimeSpan startAt = _pendingSeek ?? pending.StartAt;
            _pendingSeek = null;
            _startSampleOffset = AudioMath.Clamp((int)(startAt.TotalSeconds * AudioClipPlayback.SamplingRate), 0, int.MaxValue);
            _endSampleOffset = pending.EndAt.HasValue
                ? AudioMath.Clamp((int)(pending.EndAt.Value.TotalSeconds * AudioClipPlayback.SamplingRate), 0, int.MaxValue)
                : -1;

            if (_currentSamples.Length > 0)
            {
                _startSampleOffset = AudioMath.Clamp(_startSampleOffset, 0, Math.Max(0, _currentSamples.Length - 1));
                if (_endSampleOffset >= 0)
                    _endSampleOffset = AudioMath.Clamp(_endSampleOffset, _startSampleOffset + 1, _currentSamples.Length);
            }

            _sampleOffset = _startSampleOffset;
            _lastSendTime = -1;
            _lastPositionSendTime = -1;
            _streamErrorLogged = false;
        }
    }

    private bool SendPacket()
    {
        if (_stream != null)
            return SendStreamPacket();

        if (_sampleOffset >= _currentSamples.Length || (_endSampleOffset >= 0 && _sampleOffset >= _endSampleOffset))
            return FinishOrLoop();

        int remaining = _currentSamples.Length - _sampleOffset;
        if (_endSampleOffset >= 0)
            remaining = Math.Min(remaining, _endSampleOffset - _sampleOffset);

        int count = Math.Min(AudioClipPlayback.PacketSize, remaining);
        Array.Copy(_currentSamples, _sampleOffset, _pcmBuffer, 0, count);

        if (count < AudioClipPlayback.PacketSize)
            Array.Clear(_pcmBuffer, count, AudioClipPlayback.PacketSize - count);

        ApplyCrossfade(count);
        ApplyVolumeAndLimit(_pcmBuffer, _volumeFactor);
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
            Log.Error($"BotAudioStreamer: stream '{CurrentPlay}' failed: {error.Message}");
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

        ApplyVolumeAndLimit(_pcmBuffer, _volumeFactor);
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
                    _stream = PcmDecoder.OpenStream(CurrentPlay, TimeSpan.FromSeconds((double)_startSampleOffset / AudioClipPlayback.SamplingRate));
                    _sampleOffset = _startSampleOffset;
                    _streamErrorLogged = false;
                    return true;
                }
                catch (Exception ex)
                {
                    Log.Warn($"BotAudioStreamer: failed to restart stream loop '{CurrentPlay}': {ex.Message}");
                }
            }
            else
            {
                _sampleOffset = _startSampleOffset;
                return true;
            }
        }

        bool hasNext;
        lock (_queueLock) hasNext = ContinueAfterTrack && _queue.Count > 0;

        string finished = CurrentPlay;

        if (hasNext)
        {
            LoadNextAsync(TimeSpan.Zero, null);
            OnTrackFinished?.Invoke(finished);
            return false;
        }

        CurrentPlay = string.Empty;
        CurrentMetadata = new AudioTrackMetadata();
        _stream?.Dispose();
        _stream = null;
        _currentSamples = Array.Empty<float>();
        _sampleOffset = 0;
        _startSampleOffset = 0;
        _endSampleOffset = -1;
        _lastSendTime = -1;
        _lastPositionSendTime = -1;
        IsPaused = false;
        OnTrackFinished?.Invoke(finished);
        return false;
    }

    private void ApplyVolumeAndLimit(float[] buffer, float volume)
    {
        for (int i = 0; i < AudioClipPlayback.PacketSize; i++)
            buffer[i] = AudioMath.SoftLimit(buffer[i] * volume);
    }

    private void SendEncodedFrame()
    {
        int encodedLen = _encoder.Encode(_pcmBuffer, _encodedBuffer);
        if (encodedLen <= 0) return;

        var msg = new VoiceMessage(Hub, Channel, _encodedBuffer, encodedLen, false);
        bool isRadio = Channel == VoiceChatChannel.Radio;
        double now = Time.unscaledTimeAsDouble;
        bool sendPos = isRadio && (_lastPositionSendTime < 0 || now - _lastPositionSendTime >= 0.5);
        IEnumerable<ReferenceHub> hubs = _frameHubs.Count > 0 ? _frameHubs : ReferenceHub.AllHubs;

        foreach (ReferenceHub hub in hubs)
        {
            if (!CanSendTo(hub)) continue;

            if (sendPos)
            {
                var posMsg = new PersonalRadioPlayback.TransmitterPositionMessage
                {
                    Transmitter = new RecyclablePlayerId(Hub),
                    WaypointId = new RelativePosition(Hub.transform.position).WaypointId
                };
                hub.connectionToClient.Send(posMsg);
            }

            if (PlayerVolumes.TryGetValue(hub.PlayerId, out float personalVolume))
            {
                Array.Copy(_pcmBuffer, _personalPcmBuffer, AudioClipPlayback.PacketSize);
                ApplyVolumeAndLimit(_personalPcmBuffer, personalVolume);
                int personalLen = GetPersonalEncoder(hub.PlayerId).Encode(_personalPcmBuffer, _personalEncodedBuffer);
                if (personalLen > 0)
                    hub.connectionToClient.Send(new VoiceMessage(Hub, Channel, _personalEncodedBuffer, personalLen, false));
            }
            else
            {
                hub.connectionToClient.Send(msg);
            }
        }

        if (sendPos)
            _lastPositionSendTime = now;
    }

    private bool CanSendTo(ReferenceHub hub)
    {
        if (hub?.connectionToClient == null || hub == Hub)
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

    private void LoadNextAsync(TimeSpan startAt, TimeSpan? endAt)
    {
        string path;
        lock (_queueLock)
        {
            if (_queue.Count == 0)
            {
                CurrentPlay = string.Empty;
                return;
            }

            if (Shuffle && _queue.Count > 1)
            {
                int idx = UnityEngine.Random.Range(0, _queue.Count);
                path = _queue[idx];
                _queue.RemoveAt(idx);
            }
            else
            {
                path = _queue[0];
                _queue.RemoveAt(0);
            }
        }

        lock (_playbackLock)
        {
            CurrentPlay = path;
            _currentSamples = Array.Empty<float>();
            _stream?.Dispose();
            _stream = null;
            _sampleOffset = 0;
            _startSampleOffset = AudioMath.Clamp((int)(startAt.TotalSeconds * AudioClipPlayback.SamplingRate), 0, int.MaxValue);
            _endSampleOffset = endAt.HasValue
                ? AudioMath.Clamp((int)(endAt.Value.TotalSeconds * AudioClipPlayback.SamplingRate), 0, int.MaxValue)
                : -1;
        }

        int version = Interlocked.Increment(ref _loadVersion);
        float pitch = _pitchShift;
        float volume = Volume;
        bool loop = Loop;

        Task.Run(() =>
        {
            PendingLoad pending = null;

            try
            {
                if (_loadVersion != version)
                    return;

                pending = CreatePendingLoad(path, pitch, startAt, endAt, version);
                pending.Volume = volume;
                pending.Loop = loop;
            }
            catch (OperationCanceledException)
            {
                pending = null;
            }
            catch (Exception ex)
            {
                Log.Error($"BotAudioStreamer: failed to load '{path}': {ex.Message}");
                pending = new PendingLoad(path, Array.Empty<float>(), null, new AudioTrackMetadata(), startAt, endAt)
                {
                    Volume = volume,
                    Loop = false,
                };
            }

            if (_loadVersion == version)
                _pendingLoad = pending;
            else
                pending.Dispose();
        });
    }

    private PendingLoad CreatePendingLoad(string path, float pitch, TimeSpan startAt, TimeSpan? endAt, int version)
    {
        if (PcmDecoder.ShouldStream(path))
            return new PendingLoad(path, Array.Empty<float>(), PcmDecoder.OpenStream(path, startAt, () => _loadVersion == version), new AudioTrackMetadata(), startAt, endAt);

        AudioClipData data = AudioClipCache.GetOrDecode(path, pitch);
        return new PendingLoad(path, data.Samples, null, data.Metadata, startAt, endAt);
    }

    private void OnDestroy()
    {
        Interlocked.Increment(ref _loadVersion);
        _pendingLoad?.Dispose();
        _pendingLoad = null;
        _stream?.Dispose();
        _stream = null;
        _encoder?.Dispose();
        _encoder = null;

        foreach (var encoder in _personalEncoders.Values)
            encoder.Dispose();

        _personalEncoders.Clear();

        if (_fadeHandle.IsRunning)
            Timing.KillCoroutines(_fadeHandle);
    }

    private sealed class PendingLoad : IDisposable
    {
        public readonly string Path;
        public readonly float[] Samples;
        public readonly AudioPcmStream Stream;
        public readonly AudioTrackMetadata Metadata;
        public readonly TimeSpan StartAt;
        public readonly TimeSpan? EndAt;
        public float Volume = 100f;
        public bool Loop;
        public float[] CrossfadeSamples;
        public int CrossfadeOffset;
        public int CrossfadeLength;

        public PendingLoad(string path, float[] samples, AudioPcmStream stream, AudioTrackMetadata metadata, TimeSpan startAt, TimeSpan? endAt)
        {
            Path = path;
            Samples = samples;
            Stream = stream;
            Metadata = metadata;
            StartAt = startAt;
            EndAt = endAt;
        }

        public void Dispose() => Stream?.Dispose();
    }
}
