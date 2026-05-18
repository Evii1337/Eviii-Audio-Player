using EviAudio.API.Preset;
using EviAudio.API.Zone;
using EviAudio.Other;
using Exiled.API.Interfaces;
using System.Collections.Generic;
using System.ComponentModel;
using VoiceChat;

namespace EviAudio;

public class Config : IConfig
{
    [Description("Enable or disable the entire plugin.")]
    public bool IsEnabled { get; set; } = true;

    [Description("Print verbose debug messages to the server console. Disable in production.")]
    public bool Debug { get; set; } = false;

    [Description("Check GitHub releases for a newer EviAudio version when the plugin starts.")]
    public bool CheckForUpdates { get; set; } = true;

    [Description("Remote admin command names. First entry is primary, rest are aliases. Example: ['audio', 'au', 'music']")]
    public string[] CommandName { get; set; } = ["audio", "au"];

    [Description("Default bot ID used by commands that can omit a bot id, such as audio play <file>, audio playfromplayers all <file>, and audio stop.")]
    public int DefaultBotId { get; set; } = 99;

    [Description("Automatically spawn bots listed in BotsList when the map generates. If false, spawn manually with: audio add <id>")]
    public bool SpawnBot { get; set; } = false;

    [Description(@"Bots auto-spawned when SpawnBot is true.
BotId 99 is the default command target.
Multiple bots with different IDs can serve different purposes.
Audio tracks stored in: EXILED/Plugins/EviAudio/tracks/")]
    public List<BotsList> BotsList { get; set; } =
    [
        new() { BotName = "EviAudio Bot", BotId = 99 }
    ];

    [Description("Enable event-driven audio: lobby music, round start/end, MTF/Chaos spawns, warhead sounds, kill/death sounds, join sounds. All clip lists below are only active when this is true.")]
    public bool SpecialEventsEnable { get; set; } = false;

    [Description("Seconds to wait after round start before any event-driven sound plays. Useful to avoid overlap with SCP reveal lines. Set to 0 to disable.")]
    public float RoundStartGraceDelay { get; set; } = 0f;

    [Description("Playlist during lobby. A random entry is picked each time a track ends. Requires SpecialEventsEnable: true. Leave empty to disable.")]
    public List<AudioFile> LobbyPlaylist { get; set; } = [];

    [Description("Played once when the round starts. Leave empty to disable.")]
    public List<AudioFile> RoundStartClip { get; set; } = [];

    [Description("Played once when the round ends. Leave empty to disable.")]
    public List<AudioFile> RoundEndClip { get; set; } = [];

    [Description("Suppress the default CASSIE NTF entrance announcement.")]
    public bool SuppressCassieMtfAnnouncement { get; set; } = false;

    [Description("Play a sound when MTF respawns. Requires SpecialEventsEnable: true.")]
    public bool MtfSpawnEnabled { get; set; } = false;

    [Description("Candidates for the MTF spawn sound (random pick). Leave empty to disable.")]
    public List<AudioFile> MtfSpawnClip { get; set; } = [];

    [Description("Play a sound when Chaos Insurgency respawns. Requires SpecialEventsEnable: true.")]
    public bool ChaosSpawnEnabled { get; set; } = false;

    [Description("Candidates for the Chaos spawn sound. Leave empty to disable.")]
    public List<AudioFile> ChaosSpawnClip { get; set; } = [];

    [Description("Play a sound when the Alpha Warhead begins arming. Requires SpecialEventsEnable: true.")]
    public bool WarheadEnabled { get; set; } = false;

    [Description("Candidates for the warhead arming sound. Leave empty to disable.")]
    public List<AudioFile> WarheadStartingClip { get; set; } = [];

    [Description("Stop the warhead sound automatically when the warhead is disarmed.")]
    public bool WarheadStopping { get; set; } = false;

    [Description("Play a separate sound when the warhead is disarmed. Requires SpecialEventsEnable: true.")]
    public bool WarheadStoppingEnabled { get; set; } = false;

    [Description("Candidates for the warhead disarm sound. Leave empty to disable.")]
    public List<AudioFile> WarheadStoppingClip { get; set; } = [];

    [Description("Played privately to the player who was killed.")]
    public List<AudioFile> PlayerDiedTargetClip { get; set; } = [];

    [Description("Played privately to the player who made the kill.")]
    public List<AudioFile> PlayerDiedKillerClip { get; set; } = [];

    [Description("Played privately to a player when they first connect to the server.")]
    public List<AudioFile> PlayerConnectedServer { get; set; } = [];

    [Description(@"Audio scene presets — named collections of SpatialAudioPlayers spawned across the map.
Activate with: audio scene activate <PresetName>
Deactivate with: audio scene deactivate <PresetName>
List all with: audio scene list

Speaker options:
  file: path relative to EviAudio/tracks/ or absolute
  volume: 0-100
  loop: true/false
  min_distance / max_distance: audible range in Unity units
  lifetime: seconds until auto-destroy (0 = permanent)
  pitch_shift: semitones (+12 = octave up, -12 = octave down, 0 = normal)
  position: explicit world coords {x, y, z}
  spawn_in_room: EXILED RoomType name — spawns in every matching room
  spawn_in_zone: ZoneType name — spawns in every room of that zone")]
    public List<AudioPreset> AudioPresets { get; set; } =
    [
        new AudioPreset
        {
            Name = "Warhead_Emergency",
            Speakers =
            [
                new PresetSpeakerConfig
                {
                    File = "siren.ogg",
                    SpawnInZone = "LightContainment",
                    Volume = 100f,
                    MinDistance = 5f,
                    MaxDistance = 30f,
                    Loop = true,
                    Lifetime = 0f,
                    PitchShift = 0f,
                },
                new PresetSpeakerConfig
                {
                    File = "siren.ogg",
                    SpawnInZone = "HeavyContainment",
                    Volume = 100f,
                    MinDistance = 5f,
                    MaxDistance = 30f,
                    Loop = true,
                    Lifetime = 0f,
                },
            ]
        },
        new AudioPreset
        {
            Name = "Horror_Basement",
            Speakers =
            [
                new PresetSpeakerConfig
                {
                    File = "horror_ambient.ogg",
                    SpawnInZone = "HeavyContainment",
                    Volume = 60f,
                    MinDistance = 3f,
                    MaxDistance = 18f,
                    Loop = true,
                    PitchShift = -2f,
                }
            ]
        },
        new AudioPreset
        {
            Name = "SCP_Breach_173",
            Speakers =
            [
                new PresetSpeakerConfig
                {
                    File = "tense_loop.ogg",
                    SpawnInRoom = "Lcz173",
                    Volume = 80f,
                    MinDistance = 4f,
                    MaxDistance = 22f,
                    Loop = true,
                }
            ]
        }
    ];

    [Description("Enable per-room ambient sounds and room-entry trigger sounds.\nAmbient speakers use SpatialAudioPlayer (true 3D positional audio).\nTrigger sounds play privately to the player who enters the room via a bot.")]
    public bool EnableAudioZones { get; set; } = false;

    [Description(@"Per-room audio zone configuration list.

Targeting:
  room_type: EXILED RoomType enum (e.g. Lcz173, HczArmory, EzGateA)
  ambient_zone: ZoneType name (LightContainment, HeavyContainment, Entrance, Surface)

Ambient speaker (3D spatial audio looped at room center):
  ambient_file: audio file path (empty = disabled)
  ambient_volume: 0.0-1.0
  ambient_min_distance / ambient_max_distance: audible range
  ambient_pitch_shift: pitch in semitones

Trigger sound (played privately to entering player):
  trigger_file: audio file path (empty = disabled)
  trigger_bot_id: which bot delivers the sound
  trigger_channel: VoiceChatChannel (Proximity, Intercom, Radio...)
  trigger_volume: 0-100
  trigger_loop: loop the trigger sound
  trigger_once_per_round: play only once per player per round")]
    public List<AudioZoneConfig> AudioZones { get; set; } =
    [
        new AudioZoneConfig
        {
            RoomType = "Lcz173",
            TriggerFile = "173_tense.ogg",
            TriggerBotId = 99,
            TriggerChannel = VoiceChatChannel.Proximity,
            TriggerVolume = 80,
            TriggerLoop = true,
            TriggerOncePerRound = true,
        },
        new AudioZoneConfig
        {
            AmbientZone = "HeavyContainment",
            AmbientFile = "hcz_ambient.ogg",
            AmbientVolume = 0.35f,
            AmbientMinDistance = 4f,
            AmbientMaxDistance = 20f,
        }
    ];

    [Description("Automatically reduce all bot volumes when CASSIE makes an announcement, then restore them. Prevents music from drowning out important in-game announcements.")]
    public bool EnableCassieDucking { get; set; } = false;

    [Description("Volume percentage (0-100) bots are reduced to during a CASSIE announcement.")]
    public float CassieDuckVolume { get; set; } = 20f;

    [Description("Seconds to fade bot volume down when CASSIE starts speaking.")]
    public float CassieDuckFadeIn { get; set; } = 0.4f;

    [Description("Seconds to fade bot volume back up after CASSIE finishes.")]
    public float CassieDuckFadeOut { get; set; } = 0.8f;

    [Description("Only duck bots broadcasting on these voice channels. Leave empty to duck ALL bots regardless of channel. Valid values: Intercom, Radio, Proximity, etc.")]
    public List<string> CassieDuckChannels { get; set; } = [];

    [Description("Enable the speaker radius visualizer. 'audio visualize' shows admin-toy spheres representing min/max speaker ranges.\nGreen sphere = min distance, blue sphere = max distance.")]
    public bool EnableSpeakerVisualizer { get; set; } = true;

    [Description("Default seconds the visualizer spheres remain visible.")]
    public float VisualizerDuration { get; set; } = 5f;

    [Description("Default volume for SpeakerToy objects spawned via 'audio speaker spawn'. Range 0.0-1.0.")]
    public float DefaultSpeakerVolume { get; set; } = 1f;

    [Description("Default minimum audible distance for SpeakerToy objects spawned via command.")]
    public float DefaultSpeakerMinDistance { get; set; } = 5f;

    [Description("Default maximum audible distance for SpeakerToy objects spawned via command.")]
    public float DefaultSpeakerMaxDistance { get; set; } = 20f;

    [Description("Default pitch shift in semitones for SpeakerToy objects spawned via command. 0 = normal.")]
    public float DefaultSpeakerPitchShift { get; set; } = 0f;

    [Description("Permission node prefix used by all EviAudio commands.\nEach subcommand requires: <PermissionPrefix>.<commandname>\nExample: 'audioplayer.play', 'audioplayer.scene', 'audioplayer.speaker'")]
    public string PermissionPrefix { get; set; } = "audioplayer";

    [Description("Maximum volume any command or config entry can set on a bot. Server-wide ceiling. Range 0-100.")]
    public float MaxVolumeCap { get; set; } = 100f;

    [Description("Decoded PCM cache memory limit in megabytes. Older tracks are evicted automatically when this limit is exceeded.")]
    public int AudioCacheMaxMegabytes { get; set; } = 512;

    [Description("Maximum audio packets a bot or spatial speaker may send in one server frame after lag. Prevents CPU spikes when the server tries to catch up.")]
    public int MaxAudioPacketsPerFrame { get; set; } = 5;

    [Description("Maximum number of simultaneously active SpatialAudioPlayers. Prevents accidental resource exhaustion from scene or speaker commands. 0 = no limit.")]
    public int MaxActiveSpeakers { get; set; } = 64;

    [Description("Maximum ambient speakers one AudioZone entry may spawn across matching rooms. 0 = no per-zone limit.")]
    public int MaxAmbientSpeakersPerZone { get; set; } = 16;

    [Description("If true, log each track start/stop to the server console at Info level. Useful for event logging. More verbose than Debug mode.")]
    public bool LogPlayback { get; set; } = false;

    [Description("If true, SpeakerToy objects spawned via 'audio speaker spawn' default to spatial (3D) mode. Can be overridden per-speaker with 'audio speaker spatial <id> false'.")]
    public bool DefaultSpeakerSpatial { get; set; } = true;

    [Description("Default lifetime in seconds for SpeakerToy objects spawned via command. 0 = permanent until manually destroyed.")]
    public float DefaultSpeakerLifetime { get; set; } = 0f;
}
