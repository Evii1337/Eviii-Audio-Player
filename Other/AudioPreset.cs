using System.Collections.Generic;
using System.ComponentModel;
using UnityEngine;

namespace EviAudio.API.Preset;

public sealed class AudioPreset
{
    [Description("Unique name used in the 'audio scene activate <name>' command.")]
    public string Name { get; set; } = "default_preset";

    [Description("List of speakers that will be spawned when this preset is activated.")]
    public List<PresetSpeakerConfig> Speakers { get; set; } = new();
}

public sealed class PresetSpeakerConfig
{
    [Description("Path to audio file, relative to EviAudio/tracks/ or absolute.")]
    public string File { get; set; } = "";

    [Description("Volume 0-100.")]
    public float Volume { get; set; } = 80f;

    [Description("Loop the track.")]
    public bool Loop { get; set; } = false;

    [Description("Minimum audible distance in Unity units.")]
    public float MinDistance { get; set; } = 5f;

    [Description("Maximum audible distance in Unity units.")]
    public float MaxDistance { get; set; } = 20f;

    [Description("Seconds until the SpatialAudioPlayer self-destructs. 0 = never.")]
    public float Lifetime { get; set; } = 0f;

    [Description("Pitch shift in semitones. Positive = higher, negative = lower. 0 = off.")]
    public float PitchShift { get; set; } = 0f;

    [Description("Explicit world position. Leave null to use SpawnInZone or SpawnInRoom.")]
    public SerializableVector3? Position { get; set; } = null;

    [Description("Spawn one speaker at the center of every room in this zone. Values: LightContainment, HeavyContainment, Entrance, Surface.")]
    public string SpawnInZone { get; set; } = "";

    [Description("Spawn a speaker at every room matching this EXILED RoomType name, e.g. Lcz173.")]
    public string SpawnInRoom { get; set; } = "";
}

public struct SerializableVector3
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }

    public Vector3 ToVector3() => new(X, Y, Z);
}
