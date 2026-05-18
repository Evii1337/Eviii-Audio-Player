using Exiled.API.Enums;
using System;
using System.ComponentModel;
using VoiceChat;

namespace EviAudio.API.Zone;

public sealed class AudioZoneConfig
{
    [Description("EXILED RoomType name to target a specific room, e.g. Lcz173, HczArmory, EzGateA.")]
    public string RoomType { get; set; } = "";

    [Description("Target all rooms in a ZoneType. Values: LightContainment, HeavyContainment, Entrance, Surface.")]
    public string AmbientZone { get; set; } = "";

    [Description("Ambient sound file looped via SpatialAudioPlayer at the room center. Empty = disabled.")]
    public string AmbientFile { get; set; } = "";

    [Description("Ambient speaker volume 0.0-1.0.")]
    public float AmbientVolume { get; set; } = 0.5f;

    [Description("Ambient speaker minimum audible distance.")]
    public float AmbientMinDistance { get; set; } = 3f;

    [Description("Ambient speaker maximum audible distance.")]
    public float AmbientMaxDistance { get; set; } = 15f;

    [Description("Ambient speaker pitch shift in semitones.")]
    public float AmbientPitchShift { get; set; } = 0f;

    [Description("Sound played via a bot when a player enters this room. Empty = disabled.")]
    public string TriggerFile { get; set; } = "";

    [Description("Bot ID used to play the trigger sound.")]
    public int TriggerBotId { get; set; } = 99;

    [Description("Voice channel for the trigger sound.")]
    public VoiceChatChannel TriggerChannel { get; set; } = VoiceChatChannel.Proximity;

    [Description("Trigger sound volume 0-100.")]
    public int TriggerVolume { get; set; } = 80;

    [Description("Loop the trigger sound.")]
    public bool TriggerLoop { get; set; } = false;

    [Description("If true, the trigger plays only once per player per round.")]
    public bool TriggerOncePerRound { get; set; } = false;

    public RoomType? ParsedRoomType { get; private set; }
    public ZoneType? ParsedZoneType { get; private set; }
    public bool EnumsParsed { get; private set; }

    public void ParseEnums()
    {
        ParsedRoomType = null;
        ParsedZoneType = null;
        EnumsParsed = true;

        if (!string.IsNullOrWhiteSpace(RoomType) && Enum.TryParse(RoomType, out RoomType roomType))
            ParsedRoomType = roomType;

        if (!string.IsNullOrWhiteSpace(AmbientZone) && Enum.TryParse(AmbientZone, out ZoneType zoneType))
            ParsedZoneType = zoneType;
    }
}
