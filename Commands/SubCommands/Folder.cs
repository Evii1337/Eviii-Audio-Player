using CommandSystem;
using EviAudio.API;
using Exiled.API.Features;
using Exiled.Permissions.Extensions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VoiceChat;

namespace EviAudio.Commands.SubCommands;

public class Folder : ICommand, IUsageProvider
{
    public string Command => "folder";
    public string[] Aliases => ["dir", "directory", "playdir"];
    public string Description => "Play all tracks from a folder on a bot, optionally shuffled or targeted.";
    public string[] Usage => ["[Bot ID|default]", "Folder Path", "[shuffle true/false]", "[volume]", "[--channel channel]", "[--target players|all]"];

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (!sender.CheckPermission($"audioplayer.{Command}"))
        {
            response = $"No permission: audioplayer.{Command}";
            return false;
        }

        if (arguments.Count == 0)
        {
            response = "Usage: audio folder [botId|default] <folder path> [shuffle] [volume] [--channel channel] [--target players|all]";
            return false;
        }

        string botValue = "default";
        int startIndex = 0;

        if (arguments.Count >= 2 && (int.TryParse(arguments.At(0), out _) || CommandTools.IsDefaultToken(arguments.At(0))))
        {
            botValue = arguments.At(0);
            startIndex = 1;
        }

        if (!CommandTools.TryGetBot(botValue, out var bot, out response))
            return false;

        float volume = bot.Volume;
        bool shuffle = false;
        VoiceChatChannel? channel = null;
        List<Player> targets = null;
        var pathParts = new List<string>();

        for (int i = startIndex; i < arguments.Count; i++)
        {
            string arg = arguments.At(i);

            if (ReadVolume(arguments, ref i, arg, out float parsedVolume, out response))
            {
                if (response != null) return false;
                volume = parsedVolume;
                continue;
            }

            if (ReadShuffle(arguments, ref i, arg, out bool parsedShuffle))
            {
                shuffle = parsedShuffle;
                continue;
            }

            if (ReadChannel(arguments, ref i, arg, out VoiceChatChannel parsedChannel, out response))
            {
                if (response != null) return false;
                channel = parsedChannel;
                continue;
            }

            if (ReadTarget(arguments, ref i, arg, out List<Player> parsedTargets, out response))
            {
                if (response != null) return false;
                targets = parsedTargets;
                continue;
            }

            pathParts.Add(arg);
        }

        if (pathParts.Count > 0 && CommandTools.TryParseBool(pathParts[pathParts.Count - 1], out bool tailShuffle))
        {
            shuffle = tailShuffle;
            pathParts.RemoveAt(pathParts.Count - 1);
        }

        if (pathParts.Count > 0 && CommandTools.TryParseVolume(pathParts[pathParts.Count - 1], out float tailVolume))
        {
            volume = tailVolume;
            pathParts.RemoveAt(pathParts.Count - 1);
        }

        if (pathParts.Count == 0)
        {
            response = "Folder path is required.";
            return false;
        }

        string rawFolder = string.Join(" ", pathParts);
        string folder = Directory.Exists(rawFolder)
            ? rawFolder
            : Path.Combine(Plugin.Instance.AudioPath, rawFolder);

        if (!Directory.Exists(folder))
        {
            response = $"Folder not found: {folder}";
            return false;
        }

        if (targets != null)
            channel ??= VoiceChatChannel.Intercom;

        bot.PlayFolder(folder, volume, shuffle, channel, targets?.Select(player => player.Id));
        response = $"Bot {bot.ID}: playing folder '{folder}' at {volume:F0}% shuffle={shuffle}.";
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

    private static bool ReadShuffle(ArraySegment<string> args, ref int index, string arg, out bool shuffle)
    {
        shuffle = true;

        if (!CommandTools.IsFlag(arg, "--shuffle", "shuffle", "random"))
            return false;

        if (index + 1 < args.Count && CommandTools.TryParseBool(args.At(index + 1), out bool parsed))
        {
            shuffle = parsed;
            index++;
        }

        return true;
    }

    private static bool ReadChannel(ArraySegment<string> args, ref int index, string arg, out VoiceChatChannel channel, out string response)
    {
        channel = default;
        response = null;

        if (!CommandTools.IsFlag(arg, "--channel", "channel", "chan", "voice"))
            return false;

        if (index + 1 >= args.Count)
        {
            response = "Missing value for --channel.";
            return true;
        }

        if (!Enum.TryParse(args.At(++index), true, out channel))
        {
            response = $"Unknown VoiceChatChannel: {args.At(index)}. Valid values: {string.Join(", ", Enum.GetNames(typeof(VoiceChatChannel)))}";
            return true;
        }

        return true;
    }

    private static bool ReadTarget(ArraySegment<string> args, ref int index, string arg, out List<Player> targets, out string response)
    {
        targets = null;
        response = null;

        if (!CommandTools.IsFlag(arg, "--target", "target", "-t"))
            return false;

        if (index + 1 >= args.Count)
        {
            response = "Missing value for --target.";
            return true;
        }

        CommandTools.TryGetPlayers(args.At(++index), out targets, out response);
        return true;
    }
}