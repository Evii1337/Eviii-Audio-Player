using CommandSystem;
using EviAudio.API.Container;
using Exiled.Permissions.Extensions;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace EviAudio.Commands.SubCommands;

public class Fade : ICommand, IUsageProvider
{
    public string Command => "fade";
    public string[] Aliases => ["fadeto", "fv"];
    public string Description => "Fade one bot, default bot, or all bots to a target volume.";
    public string[] Usage => ["[Bot ID|default|all]", "Target Volume (0-100)", "Duration (seconds)"];

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (!sender.CheckPermission($"audioplayer.{Command}"))
        {
            response = $"No permission: audioplayer.{Command}";
            return false;
        }

        if (arguments.Count < 2)
        {
            response = "Usage: audio fade [botId|default|all] <target volume 0-100> <duration seconds>";
            return false;
        }

        string target;
        string volumeValue;
        string durationValue;

        if (arguments.Count == 2)
        {
            target = "default";
            volumeValue = arguments.At(0);
            durationValue = arguments.At(1);
        }
        else
        {
            target = arguments.At(0);
            volumeValue = arguments.At(1);
            durationValue = arguments.At(2);
        }

        if (!CommandTools.TryParseVolume(volumeValue, out float volume))
        {
            response = "Target volume must be a number between 0 and 100.";
            return false;
        }

        if (!float.TryParse(durationValue, NumberStyles.Float, CultureInfo.InvariantCulture, out float duration) || duration <= 0f)
        {
            response = "Duration must be a positive number (seconds).";
            return false;
        }

        if (!CommandTools.TryGetBots(target, out List<AudioPlayerBot> bots, out response))
            return false;

        foreach (var bot in bots)
            bot.FadeTo(volume, duration);

        response = bots.Count == 1 ? $"Bot {bots[0].ID}: fading volume to {volume:F0}% over {duration:F1}s." : $"Fading {bots.Count} bot(s) to {volume:F0}% over {duration:F1}s: {CommandTools.BotNames(bots)}.";
        return true;
    }
}