using CommandSystem;
using EviAudio.API;
using EviAudio.Other;
using Exiled.Permissions.Extensions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace EviAudio.Commands.SubCommands;

public class Crossfade : ICommand, IUsageProvider
{
    public string Command => "crossfade";
    public string[] Aliases => ["xfade"];
    public string Description => "Crossfade a bot into another track. Bot ID can be omitted for the default bot.";
    public string[] Usage => ["[Bot ID|default]", "Path", "[Volume 0-100]", "[Loop true/false]"];

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (!sender.CheckPermission($"audioplayer.{Command}"))
        {
            response = $"No permission: audioplayer.{Command}";
            return false;
        }

        if (arguments.Count == 0)
        {
            response = "Usage: audio crossfade [botId|default] <path> [volume] [loop]";
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
        bool loop = bot.Loop;
        var pathParts = arguments.Skip(startIndex).ToList();

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

        bot.CrossfadeTo(path, volume, loop);
        response = $"Bot {bot.ID}: crossfading to '{Path.GetFileName(path)}' at {volume:F0}% loop={loop}.";
        return true;
    }
}