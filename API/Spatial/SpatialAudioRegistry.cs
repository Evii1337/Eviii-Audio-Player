using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace EviAudio.API.Spatial;

public static class SpatialAudioRegistry
{
    private static readonly ConcurrentDictionary<int, SpatialAudioPlayer> _players = new();
    private static readonly object IdLock = new();
    private static int _nextId;

    public static IReadOnlyDictionary<int, SpatialAudioPlayer> All => _players;

    internal static int Register(SpatialAudioPlayer player)
    {
        while (true)
        {
            int id = NextId();
            if (_players.TryAdd(id, player))
                return id;
        }
    }

    internal static void Unregister(int id) => _players.TryRemove(id, out _);

    public static SpatialAudioPlayer Get(int id)
        => _players.TryGetValue(id, out var p) ? p : null;

    internal static void Clear()
    {
        _players.Clear();
        _nextId = 0;
    }

    private static int NextId()
    {
        while (true)
        {
            int id = Interlocked.Increment(ref _nextId);
            if (id > 0)
                return id;

            lock (IdLock)
            {
                if (_nextId < 0)
                    _nextId = 0;
            }
        }
    }
}
