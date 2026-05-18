using EviAudio.API.Spatial;
using EviAudio.Other;
using Exiled.API.Features;
using Exiled.API.Enums;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace EviAudio.API.Preset;

public static class SceneManager
{
    private static readonly ConcurrentDictionary<string, List<SpatialAudioPlayer>> _activeScenes = new();

    public static IReadOnlyDictionary<string, List<SpatialAudioPlayer>> ActiveScenes => _activeScenes;

    public static (bool success, string message, List<SpatialAudioPlayer> players) ActivateScene(string presetName)
    {
        if (Plugin.Instance?.Config?.AudioPresets == null)
            return (false, "AudioPresets config is null.", null);

        var preset = Plugin.Instance.Config.AudioPresets
            .FirstOrDefault(p => p.Name.Equals(presetName, StringComparison.OrdinalIgnoreCase));

        if (preset == null)
            return (false, $"Preset '{presetName}' not found.", null);

        if (_activeScenes.ContainsKey(presetName))
            DeactivateScene(presetName);

        var players = new List<SpatialAudioPlayer>();

        foreach (var speaker in preset.Speakers)
        {
            string resolvedPath = Extensions.PathCheck(speaker.File);
            if (!File.Exists(resolvedPath))
            {
                Log.Warn($"Scene '{presetName}': file not found '{resolvedPath}', skipping.");
                continue;
            }

            var positions = ResolvePositions(speaker);
            foreach (var pos in positions)
            {
                var player = SpatialAudioPlayer.Create(pos, speaker.Volume / 100f, true, speaker.MinDistance, speaker.MaxDistance);
                if (player == null) continue;

                player.PitchShift = speaker.PitchShift;
                player.Lifetime = speaker.Lifetime;
                player.Play(resolvedPath, speaker.Volume / 100f, speaker.Loop);
                players.Add(player);
            }
        }

        if (players.Count > 0)
            _activeScenes[presetName] = players;

        return (true, $"Scene '{presetName}': spawned {players.Count} speaker(s).", players);
    }

    public static (bool success, string message) DeactivateScene(string presetName)
    {
        if (!_activeScenes.TryGetValue(presetName, out var players))
            return (false, $"Scene '{presetName}' is not active.");

        foreach (var player in players)
            try { player.DestroySelf(); } catch { }

        _activeScenes.TryRemove(presetName, out _);
        return (true, $"Scene '{presetName}' deactivated. {players.Count} speaker(s) removed.");
    }

    public static void DeactivateAll()
    {
        foreach (var kvp in _activeScenes)
            foreach (var player in kvp.Value)
                try { player.DestroySelf(); } catch { }

        _activeScenes.Clear();
    }

    private static List<Vector3> ResolvePositions(PresetSpeakerConfig speaker)
    {
        var result = new List<Vector3>();

        if (speaker.Position.HasValue)
        {
            result.Add(speaker.Position.Value.ToVector3());
            return result;
        }

        if (!string.IsNullOrWhiteSpace(speaker.SpawnInRoom))
        {
            if (Enum.TryParse(speaker.SpawnInRoom, out RoomType roomType))
                foreach (var room in Room.List.Where(r => r.Type == roomType))
                    result.Add(room.Position + Vector3.up);
            else
                Log.Warn($"SceneManager: unknown RoomType '{speaker.SpawnInRoom}'.");
            return result;
        }

        if (!string.IsNullOrWhiteSpace(speaker.SpawnInZone))
        {
            if (Enum.TryParse(speaker.SpawnInZone, out ZoneType zone))
                foreach (var room in Room.List.Where(r => r.Zone == zone))
                    result.Add(room.Position + Vector3.up);
            else
                Log.Warn($"SceneManager: unknown ZoneType '{speaker.SpawnInZone}'.");
            return result;
        }

        Log.Warn("SceneManager: speaker has no position, SpawnInRoom, or SpawnInZone. Skipping.");
        return result;
    }
}
