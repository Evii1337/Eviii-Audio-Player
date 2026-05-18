using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;

namespace EviAudio.API;

public static class AudioClipCache
{
    private sealed class Entry
    {
        public string Key;
        public AudioClipData Data;
        public long Bytes;
        public LinkedListNode<string> Node;
    }

    private static readonly Dictionary<string, Entry> Entries = new();
    private static readonly ConcurrentDictionary<string, Lazy<AudioClipData>> Inflight = new();
    private static readonly LinkedList<string> Lru = new();
    private static readonly object Lock = new();
    private static long _bytes;

    public static long MaxBytes { get; set; } = 512L * 1024L * 1024L;

    public static long TotalBytes
    {
        get
        {
            lock (Lock)
                return _bytes;
        }
    }

    public static int Count
    {
        get
        {
            lock (Lock)
                return Entries.Count;
        }
    }

    public static AudioClipData GetOrDecode(string path, float pitchShift = 0f)
    {
        bool isUrl = PcmDecoder.IsUrl(path);
        string decodePath = isUrl ? path : Path.GetFullPath(path);
        long stamp = !isUrl && File.Exists(decodePath) ? File.GetLastWriteTimeUtc(decodePath).Ticks : 0L;
        string key = (isUrl ? "url|" : "file|") + decodePath + "|" + stamp + "|" + pitchShift.ToString("0.###", CultureInfo.InvariantCulture);

        lock (Lock)
        {
            if (Entries.TryGetValue(key, out var entry))
            {
                Lru.Remove(entry.Node);
                Lru.AddFirst(entry.Node);
                return entry.Data;
            }
        }

        var lazy = Inflight.GetOrAdd(
            key,
            _ => new Lazy<AudioClipData>(
                () => PcmDecoder.DecodeFile(decodePath, decodePath, pitchShift),
                LazyThreadSafetyMode.ExecutionAndPublication));

        AudioClipData data;

        try
        {
            data = lazy.Value;
        }
        finally
        {
            Inflight.TryRemove(key, out _);
        }

        long bytes = data.Samples.LongLength * sizeof(float);

        lock (Lock)
        {
            if (Entries.TryGetValue(key, out var existing))
            {
                Lru.Remove(existing.Node);
                Lru.AddFirst(existing.Node);
                return existing.Data;
            }

            var node = new LinkedListNode<string>(key);
            Lru.AddFirst(node);
            Entries[key] = new Entry { Key = key, Data = data, Bytes = bytes, Node = node };
            _bytes += bytes;
            Trim();
            return data;
        }
    }

    public static void Clear()
    {
        lock (Lock)
        {
            Entries.Clear();
            Inflight.Clear();
            Lru.Clear();
            _bytes = 0;
        }
    }

    private static void Trim()
    {
        while (_bytes > MaxBytes && Lru.Last != null)
        {
            string key = Lru.Last.Value;
            Lru.RemoveLast();

            if (!Entries.TryGetValue(key, out var entry))
                continue;

            Entries.Remove(key);
            _bytes -= entry.Bytes;
        }
    }
}
