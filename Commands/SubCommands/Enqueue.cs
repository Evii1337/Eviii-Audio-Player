using CommandSystem;
using EviAudio.API;
using EviAudio.Other;
using Exiled.Permissions.Extensions;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace EviAudio.Commands.SubCommands;

public class Enqueue : ICommand, IUsageProvider
{
    public string Command => "enqueue";
    public string[] Aliases => [];
    public string Description => "Add a track to a bot queue. Bot ID can be omitted for the default bot.";
    public string[] Usage => ["[Bot ID|default]", "Path", "[Position (-1 = end)]"];

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (!sender.CheckPermission($"audioplayer.{Command}"))
        {
            response = $"No permission: audioplayer.{Command}";
            return false;
        }

        if (arguments.Count == 0)
        {
            response = "Usage: audio enqueue [botId|default] <path> [position]";
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

        int position = -1;
        var pathParts = new List<string>();

        for (int i = startIndex; i < arguments.Count; i++)
        {
            string arg = arguments.At(i);

            if (CommandTools.IsFlag(arg, "--position", "position", "pos", "-p"))
            {
                if (i + 1 >= arguments.Count || !int.TryParse(arguments.At(i + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out position))
                {
                    response = "Position must be a number.";
                    return false;
                }

                i++;
                continue;
            }

            if (CommandTools.IsFlag(arg, "--next", "next"))
            {
                position = 0;
                continue;
            }

            pathParts.Add(arg);
        }

        if (pathParts.Count > 1 && int.TryParse(pathParts[pathParts.Count - 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int tailPosition))
        {
            position = tailPosition;
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

        bot.Enqueue(path, position);
        response = $"Bot {bot.ID}: queued '{Path.GetFileName(path)}' at position {position}.";
        return true;
    }
}