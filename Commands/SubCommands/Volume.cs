using CommandSystem;
using EviAudio.API.Container;
using Exiled.Permissions.Extensions;
using System;
using System.Collections.Generic;

namespace EviAudio.Commands.SubCommands;

public class Volume : ICommand, IUsageProvider
{
    public string Command => "volume";
    public string[] Aliases => ["vol", "v"];
    public string Description => "Set playback volume for a bot, default bot, or all bots.";
    public string[] Usage => ["[Bot ID|default|all]", "Volume (0-100)"];

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (!sender.CheckPermission($"audioplayer.{Command}"))
        {
            response = $"No permission: audioplayer.{Command}";
            return false;
        }

        if (arguments.Count == 0)
        {
            response = "Usage: audio volume [botId|default|all] <0-100>";
            return false;
        }

        string target;
        string volumeValue;

        if (arguments.Count == 1)
        {
            target = "default";
            volumeValue = arguments.At(0);
        }
        else
        {
            target = arguments.At(0);
            volumeValue = arguments.At(1);
        }

        if (!CommandTools.TryParseVolume(volumeValue, out float volume))
        {
            response = "Volume must be a number between 0 and 100.";
            return false;
        }

        if (!CommandTools.TryGetBots(target, out List<AudioPlayerBot> bots, out response))
            return false;

        foreach (var bot in bots)
            bot.Volume = volume;

        response = bots.Count == 1 ? $"Bot {bots[0].ID}: volume set to {volume:F0}%." : $"Set volume to {volume:F0}% for {bots.Count} bot(s): {CommandTools.BotNames(bots)}.";
        return true;
    }
}