using CommandSystem;
using EviAudio.API;
using EviAudio.Other;
using Exiled.API.Features;
using Exiled.Permissions.Extensions;
using PlayerRoles;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using VoiceChat;

namespace EviAudio.Commands.SubCommands;

public class PFP : ICommand, IUsageProvider
{
    public string Command => "playfromplayers";
    public string[] Aliases => ["pfp", "personal", "playpersonal"];
    public string Description => "Play audio only for selected players. Use all to target every online non-bot player.";
    public string[] Usage => ["[Bot ID|default]", "all|Player1.Player2|--role Role", "Path", "[Volume 0-100]", "[Loop true/false]", "[--exclude player]", "[--channel channel]"];

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (!sender.CheckPermission($"audioplayer.{Command}"))
        {
            response = $"No permission: audioplayer.{Command}";
            return false;
        }

        if (arguments.Count < 2)
        {
            response = "Usage: audio playfromplayers [botId|default] <all|players|--role role> <path> [volume] [loop]";
            return false;
        }

        int id = CommandTools.ResolveDefaultBotId();
        int startIndex = 0;
        bool spawnIfMissing = true;

        if (arguments.Count >= 3)
        {
            string first = arguments.At(0);
            if (int.TryParse(first, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedId))
            {
                id = parsedId;
                startIndex = 1;
                spawnIfMissing = false;
            }
            else if (CommandTools.IsDefaultToken(first))
            {
                startIndex = 1;
            }
        }

        float volume = 100f;
        bool loop = false;
        VoiceChatChannel? channel = VoiceChatChannel.Intercom;
        List<Player> targets = null;
        var excluded = new HashSet<int>();
        var pathParts = new List<string>();

        for (int i = startIndex; i < arguments.Count; i++)
        {
            string arg = arguments.At(i);

            if (ReadTarget(arguments, ref i, arg, ref targets, out response))
            {
                if (response != null) return false;
                continue;
            }

            if (ReadRole(arguments, ref i, arg, ref targets, out response))
            {
                if (response != null) return false;
                continue;
            }

            if (ReadExclude(arguments, ref i, arg, excluded, out response))
            {
                if (response != null) return false;
                continue;
            }

            if (ReadVolume(arguments, ref i, arg, out float parsedVolume, out response))
            {
                if (response != null) return false;
                volume = parsedVolume;
                continue;
            }

            if (ReadLoop(arguments, ref i, arg, out bool parsedLoop, out response))
            {
                if (response != null) return false;
                loop = parsedLoop;
                continue;
            }

            if (ReadChannel(arguments, ref i, arg, out VoiceChatChannel? parsedChannel, out response))
            {
                if (response != null) return false;
                channel = parsedChannel;
                continue;
            }

            if (targets == null && TryReadTargetValue(arg, out var parsedTargets, out response))
            {
                if (response != null) return false;
                MergeTargets(ref targets, parsedTargets);
                continue;
            }

            pathParts.Add(arg);
        }

        if (pathParts.Count > 0 && CommandTools.TryParseBool(pathParts[pathParts.Count - 1], out bool tailLoop))
        {
            loop = tailLoop;
            pathParts.RemoveAt(pathParts.Count - 1);
        }

        if (pathParts.Count > 0 && CommandTools.TryParseVolume(pathParts[pathParts.Count - 1], out float tailVolume))
        {
            volume = tailVolume;
            pathParts.RemoveAt(pathParts.Count - 1);
        }

        if (targets == null || targets.Count == 0)
        {
            response = "Target is required. Use all, a player list, --target, or --role.";
            return false;
        }

        targets = targets
            .Where(player => player != null && player.IsConnected && !excluded.Contains(player.Id) && !player.IsAudioPlayer())
            .GroupBy(player => player.Id)
            .Select(group => group.First())
            .ToList();

        if (targets.Count == 0)
        {
            response = "No matching online players found.";
            return false;
        }

        if (pathParts.Count == 0)
        {
            response = "Path is required.";
            return false;
        }

        string rawPath = string.Join(" ", pathParts);
        string path = PcmDecoder.IsUrl(rawPath) ? rawPath : Extensions.PathCheck(rawPath);

        if (!PcmDecoder.IsUrl(path) && !File.Exists(path))
        {
            response = $"File not found: {path}";
            return false;
        }

        var bot = spawnIfMissing
            ? AudioController.GetOrSpawnAudioPlayerContainer(id)
            : AudioController.TryGetAudioPlayerContainer(id);

        if (bot == null)
        {
            response = spawnIfMissing
                ? $"Unable to spawn or find audio source bot {id}."
                : $"Bot with ID {id} not found.";
            return false;
        }

        bot.PlayFile(path, volume, loop, channel, targets.Select(player => player.Id));
        response = $"Bot {bot.ID}: playing '{Path.GetFileName(path)}' personally for {targets.Count} player(s) at {volume:F0}% loop={loop}.";
        return true;
    }

    private static bool ReadTarget(ArraySegment<string> args, ref int index, string arg, ref List<Player> targets, out string response)
    {
        response = null;

        if (!CommandTools.IsFlag(arg, "--target", "target", "-t"))
            return false;

        if (index + 1 >= args.Count)
        {
            response = "Missing value for --target.";
            return true;
        }

        if (!CommandTools.TryGetPlayers(args.At(++index), out var parsedTargets, out response))
            return true;

        MergeTargets(ref targets, parsedTargets);
        return true;
    }

    private static bool ReadRole(ArraySegment<string> args, ref int index, string arg, ref List<Player> targets, out string response)
    {
        response = null;

        if (!CommandTools.IsFlag(arg, "--role", "role", "-r"))
            return false;

        if (index + 1 >= args.Count)
        {
            response = "Missing value for --role.";
            return true;
        }

        var roles = new HashSet<RoleTypeId>();
        foreach (string raw in args.At(++index).Split('.', ',', ';'))
        {
            if (!Enum.TryParse(raw.Trim(), true, out RoleTypeId role))
            {
                response = $"Role '{raw}' not found.";
                return true;
            }

            roles.Add(role);
        }

        MergeTargets(ref targets, CommandTools.GetPlayablePlayers().Where(player => roles.Contains(player.Role.Type)).ToList());
        return true;
    }

    private static bool ReadExclude(ArraySegment<string> args, ref int index, string arg, HashSet<int> excluded, out string response)
    {
        response = null;

        if (!CommandTools.IsFlag(arg, "--exclude", "exclude", "-x"))
            return false;

        if (index + 1 >= args.Count)
        {
            response = "Missing value for --exclude.";
            return true;
        }

        if (!CommandTools.TryGetPlayers(args.At(++index), out var players, out response, allowAll: false))
            return true;

        foreach (var player in players)
            excluded.Add(player.Id);

        return true;
    }

    private static bool ReadVolume(ArraySegment<string> args, ref int index, string arg, out float volume, out string response)
    {
        volume = default;
        response = null;

        if (!CommandTools.IsFlag(arg, "--volume", "volume", "vol", "-v"))
            return false;

        if (index + 1 >= args.Count || !CommandTools.TryParseVolume(args.At(index + 1), out volume))
        {
            response = "Volume must be between 0 and 100.";
            return true;
        }

        index++;
        return true;
    }

    private static bool ReadLoop(ArraySegment<string> args, ref int index, string arg, out bool loop, out string response)
    {
        loop = true;
        response = null;

        if (!CommandTools.IsFlag(arg, "--loop", "loop"))
            return false;

        if (index + 1 < args.Count && CommandTools.TryParseBool(args.At(index + 1), out bool parsed))
        {
            loop = parsed;
            index++;
        }

        return true;
    }

    private static bool ReadChannel(ArraySegment<string> args, ref int index, string arg, out VoiceChatChannel? channel, out string response)
    {
        channel = null;
        response = null;

        if (!CommandTools.IsFlag(arg, "--channel", "channel", "chan", "--voice", "voice"))
            return false;

        if (index + 1 >= args.Count)
        {
            response = "Missing value for --channel.";
            return true;
        }

        string value = args.At(++index);
        if (value.Equals("current", StringComparison.OrdinalIgnoreCase) || value.Equals("keep", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!Enum.TryParse(value, true, out VoiceChatChannel parsed))
        {
            response = $"Unknown VoiceChatChannel: {value}. Valid values: {string.Join(", ", Enum.GetNames(typeof(VoiceChatChannel)))}";
            return true;
        }

        channel = parsed;
        return true;
    }

    private static bool TryReadTargetValue(string value, out List<Player> targets, out string response)
    {
        targets = null;
        response = null;

        bool targetLike = CommandTools.IsAllToken(value)
                          || value.IndexOfAny(['.', ',', ';']) >= 0
                          || Player.Get(value) != null
                          || int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);

        if (!targetLike)
            return false;

        if (!CommandTools.TryGetPlayers(value, out targets, out response))
            return true;

        return true;
    }

    private static void MergeTargets(ref List<Player> targets, List<Player> incoming)
    {
        targets ??= new List<Player>();

        foreach (var player in incoming)
            if (player != null && targets.All(existing => existing.Id != player.Id))
                targets.Add(player);
    }
}