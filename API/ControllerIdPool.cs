using System;
using System.Collections.Generic;

namespace EviAudio.API;

public static class ControllerIdPool
{
    private static readonly bool[] Used = new bool[256];
    private static readonly Dictionary<int, string> Owners = new();
    private static readonly object Lock = new();
    private static byte _cursor = 1;

    public static byte Allocate(string owner)
    {
        lock (Lock)
        {
            for (int attempts = 0; attempts < 255; attempts++)
            {
                byte id = _cursor;
                _cursor = _cursor == 255 ? (byte)1 : (byte)(_cursor + 1);

                if (Used[id]) continue;

                Used[id] = true;
                Owners[id] = owner;
                return id;
            }
        }

        throw new InvalidOperationException("All audio controller IDs are in use.");
    }

    public static bool TryReserve(int id, string owner)
    {
        if (id <= 0 || id > 255)
            return false;

        lock (Lock)
        {
            if (Used[id])
                return false;

            Used[id] = true;
            Owners[id] = owner;
            return true;
        }
    }

    public static void Release(int id)
    {
        if (id < 0 || id > 255)
            return;

        lock (Lock)
        {
            Used[id] = false;
            Owners.Remove(id);
        }
    }

    public static int UsedCount
    {
        get
        {
            lock (Lock)
                return Owners.Count;
        }
    }

    public static IReadOnlyDictionary<int, string> Snapshot()
    {
        lock (Lock)
            return new Dictionary<int, string>(Owners);
    }

    internal static void Clear()
    {
        lock (Lock)
        {
            Array.Clear(Used, 0, Used.Length);
            Owners.Clear();
            _cursor = 1;
        }
    }
}
