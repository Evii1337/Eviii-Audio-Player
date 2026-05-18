using EviAudio.API.Spatial;
using EviAudio.Other;
using Exiled.API.Enums;
using Exiled.API.Features;
using Exiled.Events.EventArgs.Player;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace EviAudio.API.Zone;

public sealed class AudioZoneManager
{
    private readonly List<SpatialAudioPlayer> _ambientPlayers = new();
    private readonly HashSet<string> _triggeredPlayers = new();
    private bool _disposed;

    public AudioZoneManager()
    {
        Exiled.Events.Handlers.Player.RoomChanged += OnRoomChanged;
        Exiled.Events.Handlers.Server.RoundEnded += OnRoundEnded;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Exiled.Events.Handlers.Player.RoomChanged -= OnRoomChanged;
        Exiled.Events.Handlers.Server.RoundEnded -= OnRoundEnded;
        CleanupAmbient();
    }

    public void SpawnAmbientSpeakers()
    {
        if (_disposed)
            return;

        CleanupAmbient();

        if (Plugin.Instance?.Config?.AudioZones == null) return;

        foreach (var zone in Plugin.Instance.Config.AudioZones)
        {
            zone.ParseEnums();

            if (string.IsNullOrEmpty(zone.AmbientFile)) continue;

            string path = Extensions.PathCheck(zone.AmbientFile);
            if (!File.Exists(path))
            {
                Log.Warn($"AudioZoneManager: ambient file not found '{path}'.");
                continue;
            }

            int maxPerZone = Plugin.Instance.Config.MaxAmbientSpeakersPerZone;
            int spawnedForZone = 0;

            foreach (var room in GetMatchingRooms(zone))
            {
                int maxActive = Plugin.Instance.Config.MaxActiveSpeakers;
                if (maxActive > 0 && SpatialAudioRegistry.All.Count >= maxActive)
                {
                    Log.Warn($"AudioZoneManager: active speaker limit reached ({maxActive}).");
                    break;
                }

                if (maxPerZone > 0 && spawnedForZone >= maxPerZone)
                    break;

                var player = SpatialAudioPlayer.Create(
                    room.Position + Vector3.up,
                    zone.AmbientVolume,
                    true,
                    zone.AmbientMinDistance,
                    zone.AmbientMaxDistance);

                if (player == null) continue;

                player.PitchShift = zone.AmbientPitchShift;
                player.Play(path, zone.AmbientVolume, true);
                _ambientPlayers.Add(player);
                spawnedForZone++;
            }
        }

        Log.Debug($"AudioZoneManager: spawned {_ambientPlayers.Count} ambient speaker(s).");
    }

    private void OnRoomChanged(RoomChangedEventArgs ev)
    {
        if (_disposed)
            return;

        if (ev.Player == null || ev.Player.IsNPC || ev.NewRoom == null) return;
        if (Plugin.Instance?.Config?.AudioZones == null) return;

        foreach (var zone in Plugin.Instance.Config.AudioZones)
        {
            if (!zone.EnumsParsed)
                zone.ParseEnums();

            if (string.IsNullOrEmpty(zone.TriggerFile)) continue;
            if (!RoomMatches(ev.NewRoom, zone)) continue;

            string triggerKey = $"{ev.Player.UserId}:{zone.RoomType}:{zone.AmbientZone}";
            if (zone.TriggerOncePerRound && _triggeredPlayers.Contains(triggerKey)) continue;

            var bot = AudioController.TryGetAudioPlayerContainer(zone.TriggerBotId);
            if (bot == null) continue;

            string path = Extensions.PathCheck(zone.TriggerFile);
            if (!File.Exists(path)) continue;

            bot.PlayFile(path, zone.TriggerVolume, zone.TriggerLoop, zone.TriggerChannel, new[] { ev.Player.Id });

            if (zone.TriggerOncePerRound)
                _triggeredPlayers.Add(triggerKey);
        }
    }

    private void OnRoundEnded(Exiled.Events.EventArgs.Server.RoundEndedEventArgs _)
    {
        _triggeredPlayers.Clear();
        CleanupAmbient();
    }

    public void CleanupAmbient()
    {
        foreach (var player in _ambientPlayers)
            try { player.DestroySelf(); } catch { }
        _ambientPlayers.Clear();
    }

    private static IEnumerable<Room> GetMatchingRooms(AudioZoneConfig zone)
    {
        if (zone.ParsedRoomType.HasValue)
            return Room.List.Where(r => r.Type == zone.ParsedRoomType.Value);

        if (zone.ParsedZoneType.HasValue)
            return Room.List.Where(r => r.Zone == zone.ParsedZoneType.Value);

        return Array.Empty<Room>();
    }

    private static bool RoomMatches(Room room, AudioZoneConfig zone)
    {
        if (zone.ParsedRoomType.HasValue)
            return room.Type == zone.ParsedRoomType.Value;

        if (zone.ParsedZoneType.HasValue)
            return room.Zone == zone.ParsedZoneType.Value;

        return false;
    }
}
