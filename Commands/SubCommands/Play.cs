using CommandSystem;
using EviAudio.API;
using EviAudio.Other;
using Exiled.Permissions.Extensions;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using VoiceChat;

namespace EviAudio.Commands.SubCommands;

public class Play : ICommand, IUsageProvider
{
    public string Command => "play";
    public string[] Aliases => ["playback", "replay"];
    public string Description => "Play a file on a bot. The bot ID can be omitted for the default bot.";
    public string[] Usage => ["[Bot ID|default]", "Path", "[Volume 0-100]", "[Loop true/false]", "[--start seconds]", "[--end seconds]", "[--channel channel]"];

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (!sender.CheckPermission($"audioplayer.{Command}"))
        {
            response = $"No permission: audioplayer.{Command}";
            return false;
        }

        if (arguments.Count == 0)
        {
            response = "Usage: audio play [botId|default] <path> [volume] [loop] [--start seconds] [--end seconds] [--channel channel]";
            return false;
        }

        int id = CommandTools.ResolveDefaultBotId();
        int startIndex = 0;

        if (int.TryParse(arguments.At(0), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedId))
        {
            id = parsedId;
            startIndex = 1;
        }
        else if (CommandTools.IsDefaultToken(arguments.At(0)))
        {
            startIndex = 1;
        }

        if (arguments.Count <= startIndex)
        {
            response = "Path is required.";
            return false;
        }

        var bot = AudioController.TryGetAudioPlayerContainer(id);
        if (bot == null)
        {
            response = $"Bot with ID {id} not found.";
            return false;
        }

        float volume = bot.Volume;
        bool loop = bot.Loop;
        TimeSpan? startAt = null;
        TimeSpan? endAt = null;
        VoiceChatChannel? channel = null;
        var pathParts = new List<string>();

        for (int i = startIndex; i < arguments.Count; i++)
        {
            string arg = arguments.At(i);

            if (TryReadSeconds(arguments, ref i, arg, "--start", "start", out TimeSpan start))
            {
                startAt = start;
                continue;
            }

            if (TryReadSeconds(arguments, ref i, arg, "--end", "end", out TimeSpan end))
            {
                endAt = end;
                continue;
            }

            if (TryReadVolume(arguments, ref i, arg, out float explicitVolume, out response))
            {
                if (response != null) return false;
                volume = explicitVolume;
                continue;
            }

            if (TryReadLoop(arguments, ref i, arg, out bool explicitLoop))
            {
                loop = explicitLoop;
                continue;
            }

            if (TryReadChannel(arguments, ref i, arg, out VoiceChatChannel explicitChannel, out response))
            {
                if (response != null) return false;
                channel = explicitChannel;
                continue;
            }

            pathParts.Add(arg);
        }

        if (pathParts.Count > 0 && CommandTools.TryParseBool(pathParts[pathParts.Count - 1], out bool parsedLoop))
        {
            loop = parsedLoop;
            pathParts.RemoveAt(pathParts.Count - 1);
        }

        if (pathParts.Count > 0 && CommandTools.TryParseVolume(pathParts[pathParts.Count - 1], out float parsedVolume))
        {
            volume = parsedVolume;
            pathParts.RemoveAt(pathParts.Count - 1);
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

        bot.PlayFile(path, volume, loop, channel, startAt: startAt, endAt: endAt);
        response = $"Bot {id}: playing '{Path.GetFileName(path)}' on {bot.VoiceChatChannel} at {volume:F0}% loop={loop}.";
        return true;
    }

    private static bool TryReadSeconds(ArraySegment<string> args, ref int index, string arg, string flag, string alias, out TimeSpan value)
    {
        value = default;

        if (!arg.Equals(flag, StringComparison.OrdinalIgnoreCase) && !arg.Equals(alias, StringComparison.OrdinalIgnoreCase))
            return false;

        if (index + 1 >= args.Count || !double.TryParse(args.At(index + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds))
            return false;

        value = TimeSpan.FromSeconds(Math.Max(0, seconds));
        index++;
        return true;
    }

    private static bool TryReadVolume(ArraySegment<string> args, ref int index, string arg, out float value, out string response)
    {
        value = default;
        response = null;

        if (!CommandTools.IsFlag(arg, "--volume", "volume", "vol", "-v"))
            return false;

        if (index + 1 >= args.Count || !CommandTools.TryParseVolume(args.At(index + 1), out value))
        {
            response = "Volume must be between 0 and 100.";
            return true;
        }

        index++;
        return true;
    }

    private static bool TryReadLoop(ArraySegment<string> args, ref int index, string arg, out bool value)
    {
        value = true;

        if (!CommandTools.IsFlag(arg, "--loop", "loop"))
            return false;

        if (index + 1 < args.Count && CommandTools.TryParseBool(args.At(index + 1), out bool parsed))
        {
            value = parsed;
            index++;
        }

        return true;
    }

    private static bool TryReadChannel(ArraySegment<string> args, ref int index, string arg, out VoiceChatChannel channel, out string response)
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
}