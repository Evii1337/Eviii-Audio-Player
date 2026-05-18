using CommandSystem;
using EviAudio.API;
using EviAudio.API.Spatial;
using EviAudio.Other;
using Exiled.API.Features;
using Exiled.Permissions.Extensions;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace EviAudio.Commands.SubCommands;

public class Speaker : ICommand, IUsageProvider
{
    public string Command => "speaker";
    public string[] Aliases => ["sp", "spk", "toy"];
    public string Description => "Full control over 3D SpeakerToy objects (spatial audio sources).";
    public string[] Usage => ["<subcommand> [args...]"];

    private static string Perm => "audioplayer.speaker";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (!sender.CheckPermission(Perm))
        {
            response = $"No permission: {Perm}";
            return false;
        }

        if (arguments.Count == 0)
        {
            response = BuildHelp();
            return false;
        }

        string sub = arguments.At(0).ToLowerInvariant();
        var args = new ArraySegment<string>(arguments.Array!, arguments.Offset + 1, arguments.Count - 1);

        return sub switch
        {
            "spawn" or "create" => Spawn(args, sender, out response),
            "destroy" or "remove" or "del" => Destroy(args, out response),
            "destroyall" or "clear" => DestroyAll(out response),
            "list" or "ls" => List(out response),
            "play" => Play(args, out response),
            "crossfade" or "xfade" => Crossfade(args, out response),
            "stop" => Stop(args, out response),
            "enqueue" or "queue" => Enqueue(args, out response),
            "seek" => Seek(args, out response),
            "loop" => SetLoop(args, out response),
            "volume" or "vol" => SetVolume(args, out response),
            "playervolume" or "pvol" or "pv" => SetPlayerVolume(args, out response),
            "fade" => Fade(args, out response),
            "pitch" or "pt" => SetPitch(args, out response),
            "mindist" or "mind" => SetMinDist(args, out response),
            "maxdist" or "maxd" => SetMaxDist(args, out response),
            "pos" or "move" or "position" => SetPos(args, out response),
            "rot" or "rotate" or "rotation" => SetRot(args, out response),
            "follow" or "attach" => Follow(args, out response),
            "spatial" => SetSpatial(args, out response),
            "lifetime" or "life" or "lt" => SetLifetime(args, out response),
            "viz" or "visualize" => Visualize(args, out response),
            "info" or "status" => Info(args, out response),
            _ => UnknownSub(sub, out response),
        };
    }

    private static SpatialAudioPlayer GetPlayer(string arg, out string err)
    {
        if (!int.TryParse(arg, out int id))
        {
            err = "Speaker ID must be a number.";
            return null;
        }
        var p = SpatialAudioRegistry.Get(id);
        if (p == null) err = $"No speaker with ID {id}.";
        else err = null;
        return p;
    }

    private static bool ParseFloat(string s, out float v, out string err)
    {
        if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) { err = null; return true; }
        err = $"'{s}' is not a valid number.";
        return false;
    }

    private static bool ParseVec3(ArraySegment<string> args, int offset, out Vector3 v, out string err)
    {
        v = Vector3.zero;
        if (args.Count < offset + 3)
        {
            err = "Expected <x> <y> <z>.";
            return false;
        }
        if (!ParseFloat(args.At(offset), out float x, out err)) return false;
        if (!ParseFloat(args.At(offset + 1), out float y, out err)) return false;
        if (!ParseFloat(args.At(offset + 2), out float z, out err)) return false;
        v = new Vector3(x, y, z);
        return true;
    }

    private static bool Spawn(ArraySegment<string> args, ICommandSender sender, out string response)
    {
        var cfg = Plugin.Instance?.Config;

        float vol = cfg?.DefaultSpeakerVolume ?? 1f;
        float minD = cfg?.DefaultSpeakerMinDistance ?? 5f;
        float maxD = cfg?.DefaultSpeakerMaxDistance ?? 20f;
        bool spatial = cfg?.DefaultSpeakerSpatial ?? true;
        float lifetime = cfg?.DefaultSpeakerLifetime ?? 0f;
        float pitch = cfg?.DefaultSpeakerPitchShift ?? 0f;

        Vector3 pos;
        bool useSenderPosition = args.Count == 0
            || args.At(0).Equals("here", StringComparison.OrdinalIgnoreCase)
            || args.At(0).Equals("me", StringComparison.OrdinalIgnoreCase);

        if (useSenderPosition)
        {
            Player senderPlayer = Player.Get(sender);
            if (senderPlayer == null)
            {
                response = "Server console has no position. Use: audio speaker spawn <x> <y> <z> [volume] [minD] [maxD] [spatial] [lifetime] [pitch]";
                return false;
            }

            pos = senderPlayer.Position;
            if (args.Count > 0)
                args = Slice(args, 1);
        }
        else
        {
            if (!ParseVec3(args, 0, out pos, out response))
            {
                response = "Usage: audio speaker spawn [here|me|<x> <y> <z>] [volume:0-1] [minD] [maxD] [spatial:true/false] [lifetime] [pitch]";
                return false;
            }

            args = Slice(args, 3);
        }

        if (args.Count > 6)
        {
            response = "Too many arguments. Usage: audio speaker spawn [here|me|<x> <y> <z>] [volume] [minD] [maxD] [spatial] [lifetime] [pitch]";
            return false;
        }

        if (args.Count > 0 && !ParseFloat(args.At(0), out vol, out response)) return false;
        if (args.Count > 1 && !ParseFloat(args.At(1), out minD, out response)) return false;
        if (args.Count > 2 && !ParseFloat(args.At(2), out maxD, out response)) return false;
        if (args.Count > 3 && !bool.TryParse(args.At(3), out spatial))
        {
            response = "spatial must be true or false.";
            return false;
        }
        if (args.Count > 4 && !ParseFloat(args.At(4), out lifetime, out response)) return false;
        if (args.Count > 5 && !ParseFloat(args.At(5), out pitch, out response)) return false;

        if (vol < 0f || vol > 1f)
        {
            response = "Volume must be between 0.0 and 1.0.";
            return false;
        }

        if (minD < 0f || maxD < 0f)
        {
            response = "Distances must be >= 0.";
            return false;
        }

        if (maxD < minD)
        {
            response = "maxD must be greater than or equal to minD.";
            return false;
        }

        if (lifetime < 0f)
        {
            response = "Lifetime must be >= 0.";
            return false;
        }

        if (pitch < -24f || pitch > 24f)
        {
            response = "Pitch must be between -24 and 24 semitones.";
            return false;
        }

        int max = cfg?.MaxActiveSpeakers ?? 0;
        if (max > 0 && SpatialAudioRegistry.All.Count >= max)
        {
            response = $"Speaker limit reached ({max}). Destroy some speakers first.";
            return false;
        }

        var player = SpatialAudioPlayer.Create(pos, vol, spatial, minD, maxD);
        if (player == null)
        {
            response = "Failed to create SpeakerToy. Check server log.";
            return false;
        }

        player.Lifetime = lifetime;
        player.PitchShift = pitch;

        response = $"Speaker #{player.RegistryId} spawned at ({pos.x:F1}, {pos.y:F1}, {pos.z:F1}).\n" +
                   $"  vol={vol:F2}  min={minD:F1}  max={maxD:F1}  spatial={spatial}  lifetime={lifetime:F0}s  pitch={pitch:F1}st";
        return true;
    }

    private static ArraySegment<string> Slice(ArraySegment<string> args, int count)
        => new(args.Array!, args.Offset + count, args.Count - count);

    private static bool Destroy(ArraySegment<string> args, out string response)
    {
        if (args.Count == 0) { response = "Usage: audio speaker destroy <ID>"; return false; }

        var p = GetPlayer(args.At(0), out response);
        if (p == null) return false;

        int id = p.RegistryId;
        p.DestroySelf();
        response = $"Speaker #{id} destroyed.";
        return true;
    }

    private static bool DestroyAll(out string response)
    {
        int count = SpatialAudioRegistry.All.Count;
        if (count == 0) { response = "No active speakers."; return true; }

        foreach (var kvp in SpatialAudioRegistry.All.ToList())
            try { kvp.Value.DestroySelf(); } catch { }

        response = $"Destroyed {count} speaker(s).";
        return true;
    }

    private static bool List(out string response)
    {
        var all = SpatialAudioRegistry.All;
        if (all.Count == 0) { response = "No active speakers."; return true; }

        var sb = new StringBuilder();
        sb.AppendLine($"\n<b>Active SpeakerToys — {all.Count}</b>\n");

        foreach (var kvp in all)
        {
            var p = kvp.Value;
            if (p?.Speaker == null) continue;

            var pos = p.Speaker.transform.position;
            string progress = p.IsPlaying ? $"{FormatTime(p.Position)} / {FormatTime(p.Duration)}" : "--:-- / --:--";
            string state = p.IsPlaying ? $"▶ {Path.GetFileName(p.CurrentFile)}  [{progress}]" : "⏹ idle";

            sb.AppendLine($"  <b>#{kvp.Key}</b>  [{state}]");
            sb.AppendLine($"    pos=({pos.x:F1},{pos.y:F1},{pos.z:F1})  vol={p.Volume:F2}  " +
                          $"min={p.Speaker.NetworkMinDistance:F1}  max={p.Speaker.NetworkMaxDistance:F1}");
            sb.AppendLine($"    spatial={p.Speaker.NetworkIsSpatial}  loop={p.Loop}  " +
                          $"pitch={p.PitchShift:F1}st  lifetime={p.Lifetime:F0}s");
        }

        response = sb.ToString();
        return true;
    }

    private static bool Play(ArraySegment<string> args, out string response)
    {
        if (args.Count < 2) { response = "Usage: audio speaker play <ID> <file> [volume:0-1] [loop:true/false]"; return false; }

        var p = GetPlayer(args.At(0), out response);
        if (p == null) return false;

        if (!ParsePathVolumeLoop(args, 1, p.Volume, p.Loop, out string file, out float vol, out bool loop, out response))
            return false;

        p.Play(file, vol, loop);
        response = $"Speaker #{p.RegistryId}: playing '{Path.GetFileName(file)}' vol={vol:F2} loop={loop}.";
        return true;
    }

    private static bool Crossfade(ArraySegment<string> args, out string response)
    {
        if (args.Count < 2) { response = "Usage: audio speaker crossfade <ID> <file> [volume:0-1] [loop:true/false]"; return false; }

        var p = GetPlayer(args.At(0), out response);
        if (p == null) return false;

        float vol = p.Volume;
        bool loop = p.Loop;
        int pathEnd = args.Count;

        if (args.Count > 2 && bool.TryParse(args.At(args.Count - 1), out bool parsedLoop))
        {
            loop = parsedLoop;
            pathEnd--;
        }

        if (pathEnd > 2 && ParseFloat(args.At(pathEnd - 1), out float parsedVolume, out string _))
        {
            vol = parsedVolume;
            pathEnd--;
        }

        string file = string.Join(" ", args.Skip(1).Take(pathEnd - 1));
        file = PcmDecoder.IsUrl(file) ? file : Extensions.PathCheck(file);

        p.CrossfadeTo(file, vol, loop);
        response = $"Speaker #{p.RegistryId}: crossfading to '{Path.GetFileName(file)}' vol={vol:F2} loop={loop}.";
        return true;
    }

    private static bool Stop(ArraySegment<string> args, out string response)
    {
        if (args.Count == 0) { response = "Usage: audio speaker stop <ID>"; return false; }

        var p = GetPlayer(args.At(0), out response);
        if (p == null) return false;

        p.Stop();
        response = $"Speaker #{p.RegistryId}: stopped.";
        return true;
    }

    private static bool Enqueue(ArraySegment<string> args, out string response)
    {
        if (args.Count < 2) { response = "Usage: audio speaker enqueue <ID> <file> [volume:0-1] [loop:true/false]"; return false; }

        var p = GetPlayer(args.At(0), out response);
        if (p == null) return false;

        if (!ParsePathVolumeLoop(args, 1, p.Volume, false, out string file, out float vol, out bool loop, out response))
            return false;

        p.Enqueue(file, vol, loop);
        response = $"Speaker #{p.RegistryId}: enqueued '{Path.GetFileName(file)}' vol={vol:F2} loop={loop}.";
        return true;
    }

    private static bool ParsePathVolumeLoop(
        ArraySegment<string> args,
        int start,
        float defaultVolume,
        bool defaultLoop,
        out string file,
        out float volume,
        out bool loop,
        out string response)
    {
        file = null;
        volume = defaultVolume;
        loop = defaultLoop;
        response = null;

        int pathEnd = args.Count;

        if (pathEnd > start && bool.TryParse(args.At(pathEnd - 1), out bool parsedLoop))
        {
            loop = parsedLoop;
            pathEnd--;
        }

        if (pathEnd > start && ParseFloat(args.At(pathEnd - 1), out float parsedVolume, out string _))
        {
            volume = parsedVolume;
            pathEnd--;
        }

        if (pathEnd <= start)
        {
            response = "File path is required.";
            return false;
        }

        if (volume < 0f || volume > 1f)
        {
            response = "Volume must be between 0.0 and 1.0.";
            return false;
        }

        string raw = string.Join(" ", args.Skip(start).Take(pathEnd - start));
        file = PcmDecoder.IsUrl(raw) ? raw : Extensions.PathCheck(raw);

        if (!PcmDecoder.IsUrl(file) && !File.Exists(file))
        {
            response = $"File not found: {file}";
            return false;
        }

        return true;
    }

    private static bool Seek(ArraySegment<string> args, out string response)
    {
        if (args.Count < 2) { response = "Usage: audio speaker seek <ID> <seconds>"; return false; }

        var p = GetPlayer(args.At(0), out response);
        if (p == null) return false;

        if (!ParseFloat(args.At(1), out float seconds, out response)) return false;
        if (seconds < 0) { response = "Seconds must be >= 0."; return false; }

        if (!p.SeekTo(TimeSpan.FromSeconds(seconds)))
        {
            response = "Seek failed. The track may not be loaded yet or FFmpeg could not reopen the stream.";
            return false;
        }

        response = $"Speaker #{p.RegistryId}: seeked to {TimeSpan.FromSeconds(seconds):m\\:ss}.";
        return true;
    }

    private static bool SetLoop(ArraySegment<string> args, out string response)
    {
        if (args.Count < 2) { response = "Usage: audio speaker loop <ID> <true/false>"; return false; }

        var p = GetPlayer(args.At(0), out response);
        if (p == null) return false;

        if (!bool.TryParse(args.At(1), out bool loop)) { response = "Expected true or false."; return false; }

        p.Loop = loop;
        response = $"Speaker #{p.RegistryId}: loop = {loop}.";
        return true;
    }

    private static bool SetPlayerVolume(ArraySegment<string> args, out string response)
    {
        if (args.Count < 3) { response = "Usage: audio speaker playervolume <ID> <player> <0-100>"; return false; }

        var p = GetPlayer(args.At(0), out response);
        if (p == null) return false;

        var player = Player.Get(args.At(1));
        if (player == null) { response = "Player not found."; return false; }

        if (!ParseFloat(args.At(2), out float vol, out response)) return false;
        if (vol < 0 || vol > 100) { response = "Volume must be 0-100."; return false; }

        p.SetPlayerVolume(player.Id, vol * 0.01f);
        response = $"Speaker #{p.RegistryId}: personal volume for {player.Nickname} set to {vol:F0}%.";
        return true;
    }

    private static bool SetVolume(ArraySegment<string> args, out string response)
    {
        if (args.Count < 2) { response = "Usage: audio speaker volume <ID> <0.0-1.0>"; return false; }

        var p = GetPlayer(args.At(0), out response);
        if (p == null) return false;

        if (!ParseFloat(args.At(1), out float vol, out response)) return false;
        if (vol < 0 || vol > 1) { response = "Volume must be 0.0-1.0."; return false; }

        p.Volume = vol;
        if (p.Speaker != null) p.Speaker.NetworkVolume = vol;
        response = $"Speaker #{p.RegistryId}: volume = {vol:F2}.";
        return true;
    }

    private static bool Fade(ArraySegment<string> args, out string response)
    {
        if (args.Count < 3) { response = "Usage: audio speaker fade <ID> <target volume 0-1> <duration seconds>"; return false; }

        var p = GetPlayer(args.At(0), out response);
        if (p == null) return false;

        if (!ParseFloat(args.At(1), out float vol, out response)) return false;
        if (!ParseFloat(args.At(2), out float dur, out response)) return false;
        if (vol < 0 || vol > 1) { response = "Volume must be 0.0-1.0."; return false; }
        if (dur <= 0) { response = "Duration must be positive."; return false; }

        p.FadeTo(vol, dur);
        response = $"Speaker #{p.RegistryId}: fading to {vol:F2} over {dur:F1}s.";
        return true;
    }

    private static bool SetPitch(ArraySegment<string> args, out string response)
    {
        if (args.Count < 2) { response = "Usage: audio speaker pitch <ID> <semitones (-24 to 24)>"; return false; }

        var p = GetPlayer(args.At(0), out response);
        if (p == null) return false;

        if (!ParseFloat(args.At(1), out float pt, out response)) return false;
        if (pt < -24 || pt > 24) { response = "Pitch must be -24 to 24 semitones."; return false; }

        p.PitchShift = pt;
        response = pt == 0
            ? $"Speaker #{p.RegistryId}: pitch reset to normal."
            : $"Speaker #{p.RegistryId}: pitch = {pt:+0.#;-0.#} semitones (applies on next track load).";
        return true;
    }

    private static bool SetMinDist(ArraySegment<string> args, out string response)
    {
        if (args.Count < 2) { response = "Usage: audio speaker mindist <ID> <distance>"; return false; }

        var p = GetPlayer(args.At(0), out response);
        if (p == null) return false;

        if (!ParseFloat(args.At(1), out float d, out response)) return false;
        if (d < 0) { response = "Distance must be >= 0."; return false; }

        p.SetMinDistance(d);
        response = $"Speaker #{p.RegistryId}: min distance = {d:F1}.";
        return true;
    }

    private static bool SetMaxDist(ArraySegment<string> args, out string response)
    {
        if (args.Count < 2) { response = "Usage: audio speaker maxdist <ID> <distance>"; return false; }

        var p = GetPlayer(args.At(0), out response);
        if (p == null) return false;

        if (!ParseFloat(args.At(1), out float d, out response)) return false;
        if (d < 0) { response = "Distance must be >= 0."; return false; }

        p.SetMaxDistance(d);
        response = $"Speaker #{p.RegistryId}: max distance = {d:F1}.";
        return true;
    }

    private static bool SetPos(ArraySegment<string> args, out string response)
    {
        if (args.Count < 4) { response = "Usage: audio speaker pos <ID> <x> <y> <z>"; return false; }

        var p = GetPlayer(args.At(0), out response);
        if (p == null) return false;

        if (!ParseVec3(args, 1, out Vector3 pos, out response)) return false;

        p.SetPosition(pos);
        response = $"Speaker #{p.RegistryId}: moved to ({pos.x:F2}, {pos.y:F2}, {pos.z:F2}).";
        return true;
    }

    private static bool SetRot(ArraySegment<string> args, out string response)
    {
        if (args.Count < 4) { response = "Usage: audio speaker rot <ID> <eulerX> <eulerY> <eulerZ>"; return false; }

        var p = GetPlayer(args.At(0), out response);
        if (p == null) return false;

        if (!ParseVec3(args, 1, out Vector3 euler, out response)) return false;

        p.SetRotation(Quaternion.Euler(euler));
        response = $"Speaker #{p.RegistryId}: rotation set to ({euler.x:F1}, {euler.y:F1}, {euler.z:F1}).";
        return true;
    }

    private static bool Follow(ArraySegment<string> args, out string response)
    {
        if (args.Count < 2) { response = "Usage: audio speaker follow <ID> <player|stop>"; return false; }

        var p = GetPlayer(args.At(0), out response);
        if (p == null) return false;

        if (args.At(1).Equals("stop", StringComparison.OrdinalIgnoreCase))
        {
            p.DetachFrom();
            response = $"Speaker #{p.RegistryId}: follow stopped.";
            return true;
        }

        var player = Player.Get(args.At(1));
        if (player == null || !player.IsConnected || player.ReferenceHub == null)
        {
            response = "Player not found or not connected.";
            return false;
        }

        p.AttachTo(player.ReferenceHub.transform);
        response = $"Speaker #{p.RegistryId}: following {player.Nickname}.";
        return true;
    }

    private static bool SetSpatial(ArraySegment<string> args, out string response)
    {
        if (args.Count < 2) { response = "Usage: audio speaker spatial <ID> <true/false>"; return false; }

        var p = GetPlayer(args.At(0), out response);
        if (p == null) return false;

        if (!bool.TryParse(args.At(1), out bool spatial)) { response = "Expected true or false."; return false; }

        p.SetSpatial(spatial);
        response = $"Speaker #{p.RegistryId}: spatial = {spatial}.";
        return true;
    }

    private static bool SetLifetime(ArraySegment<string> args, out string response)
    {
        if (args.Count < 2) { response = "Usage: audio speaker lifetime <ID> <seconds (0=permanent)>"; return false; }

        var p = GetPlayer(args.At(0), out response);
        if (p == null) return false;

        if (!ParseFloat(args.At(1), out float lt, out response)) return false;
        if (lt < 0) { response = "Lifetime must be >= 0."; return false; }

        p.Lifetime = lt;
        response = lt <= 0
            ? $"Speaker #{p.RegistryId}: lifetime set to permanent."
            : $"Speaker #{p.RegistryId}: self-destructs in {lt:F1}s.";
        return true;
    }

    private static bool Visualize(ArraySegment<string> args, out string response)
    {
        float duration = Plugin.Instance?.Config?.VisualizerDuration ?? 5f;

        if (args.Count == 0 || args.At(0).Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Count > 1 && ParseFloat(args.At(1), out float d, out string _)) duration = d;
            int count = SpatialAudioRegistry.All.Count;
            if (count == 0) { response = "No active speakers to visualize."; return true; }
            SpeakerVisualizer.ShowAll(duration);
            response = $"Visualizing {count} speaker(s) for {duration:F1}s.";
            return true;
        }

        var p = GetPlayer(args.At(0), out response);
        if (p?.Speaker == null) return false;
        if (args.Count > 1 && ParseFloat(args.At(1), out float dur, out string _)) duration = dur;

        SpeakerVisualizer.Show(
            p.Speaker.transform.position,
            p.Speaker.NetworkMinDistance,
            p.Speaker.NetworkMaxDistance,
            duration);

        response = $"Visualizing speaker #{p.RegistryId} for {duration:F1}s.";
        return true;
    }

    private static bool Info(ArraySegment<string> args, out string response)
    {
        if (args.Count == 0) { response = "Usage: audio speaker info <ID>"; return false; }

        var p = GetPlayer(args.At(0), out response);
        if (p == null) return false;

        var pos = p.Speaker != null ? p.Speaker.transform.position : Vector3.zero;
        var rot = p.Speaker != null ? p.Speaker.transform.rotation.eulerAngles : Vector3.zero;

        var sb = new StringBuilder();
        sb.AppendLine($"\n<b>Speaker #{p.RegistryId}</b>");
        sb.AppendLine($"  Position:   ({pos.x:F2}, {pos.y:F2}, {pos.z:F2})");
        sb.AppendLine($"  Rotation:   ({rot.x:F1}, {rot.y:F1}, {rot.z:F1})");
        sb.AppendLine($"  Volume:     {p.Volume:F3}");
        sb.AppendLine($"  Min dist:   {(p.Speaker?.NetworkMinDistance ?? 0):F1}");
        sb.AppendLine($"  Max dist:   {(p.Speaker?.NetworkMaxDistance ?? 0):F1}");
        sb.AppendLine($"  Spatial:    {(p.Speaker?.NetworkIsSpatial ?? false)}");
        sb.AppendLine($"  Loop:       {p.Loop}");
        sb.AppendLine($"  Pitch:      {p.PitchShift:F1} semitones");
        sb.AppendLine($"  Lifetime:   {(p.Lifetime <= 0 ? "permanent" : $"{p.Lifetime:F1}s")}");
        sb.AppendLine($"  Playing:    {(p.IsPlaying ? Path.GetFileName(p.CurrentFile) : "idle")}");
        sb.AppendLine($"  Position:   {p.Position:m\\:ss} / {(p.Duration <= TimeSpan.Zero ? "--:--" : p.Duration.ToString(@"m\:ss"))}");
        sb.AppendLine($"  Listeners:  {p.GetListenerCount()}");

        response = sb.ToString();
        return true;
    }

    private static string FormatTime(TimeSpan time)
    {
        if (time <= TimeSpan.Zero)
            return "--:--";

        return time.TotalHours >= 1
            ? time.ToString(@"h\:mm\:ss")
            : time.ToString(@"m\:ss");
    }

    private static bool UnknownSub(string sub, out string response)
    {
        response = $"Unknown sub-command '{sub}'. Run 'audio speaker' for help.";
        return false;
    }

    private static string BuildHelp()
    {
        return "\n<b>audio speaker</b> — SpeakerToy control\n\n" +
               "  <b>spawn</b> [here|me|<x> <y> <z>] [vol] [minD] [maxD] [spatial] [lifetime] [pitch]\n" +
               "    Create a new 3D speaker at your position or at world coordinates.\n\n" +
               "  <b>play</b> <ID> <file> [volume] [loop]\n" +
               "    Play an audio file on a speaker.\n\n" +
               "  <b>crossfade</b> <ID> <file> [volume] [loop]\n" +
               "    Crossfade into another track.\n\n" +
               "  <b>stop</b> <ID>\n" +
               "    Stop playback on a speaker.\n\n" +
               "  <b>enqueue</b> <ID> <file> [volume] [loop]\n" +
               "    Queue a track on a speaker.\n\n" +
               "  <b>loop</b> <ID> <true/false>\n" +
               "    Enable or disable looping.\n\n" +
               "  <b>volume</b> <ID> <0.0-1.0>\n" +
               "    Set speaker volume.\n\n" +
               "  <b>fade</b> <ID> <target vol 0-1> <duration seconds>\n" +
               "    Smoothly change volume.\n\n" +
               "  <b>seek</b> <ID> <seconds>\n" +
               "    Seek local decoded playback.\n\n" +
               "  <b>playervolume</b> <ID> <player> <0-100>\n" +
               "    Set personal volume for one listener.\n\n" +
               "  <b>pitch</b> <ID> <semitones -24..24>\n" +
               "    Pitch shift (applies on next track load).\n\n" +
               "  <b>mindist</b> <ID> <distance> — Set minimum audible distance.\n" +
               "  <b>maxdist</b> <ID> <distance> — Set maximum audible distance.\n" +
               "  <b>pos</b> <ID> <x> <y> <z> — Move speaker to world position.\n" +
               "  <b>rot</b> <ID> <x> <y> <z> — Set speaker rotation (Euler angles).\n" +
               "  <b>follow</b> <ID> <player|stop> — Attach speaker to a player.\n" +
               "  <b>spatial</b> <ID> <true/false> — Toggle 3D vs global audio.\n" +
               "  <b>lifetime</b> <ID> <seconds> — Auto-destroy after N seconds (0 = off).\n" +
               "  <b>viz</b> [ID|all] [duration] — Show radius spheres.\n" +
               "  <b>info</b> <ID> — Show all properties.\n" +
               "  <b>list</b> — List all active speakers.\n" +
               "  <b>destroy</b> <ID> — Destroy a speaker.\n" +
               "  <b>destroyall</b> — Destroy all speakers.\n";
    }
}
