using CommandSystem;
using EviAudio.API.Container;
using Exiled.Permissions.Extensions;
using System;
using System.Collections.Generic;

namespace EviAudio.Commands.SubCommands;

public class Loop : ICommand, IUsageProvider
{
    public string Command => "loop";
    public string[] Aliases => [];
    public string Description => "Set or toggle loop playback for a bot, default bot, or all bots.";
    public string[] Usage => ["[Bot ID|default|all]", "[true/false|toggle]"];

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (!sender.CheckPermission($"audioplayer.{Command}"))
        {
            response = $"No permission: audioplayer.{Command}";
            return false;
        }

        string target = "default";
        string value = "toggle";

        if (arguments.Count == 1)
        {
            if (CommandTools.TryParseBool(arguments.At(0), out _) || arguments.At(0).Equals("toggle", StringComparison.OrdinalIgnoreCase))
                value = arguments.At(0);
            else
                target = arguments.At(0);
        }
        else if (arguments.Count >= 2)
        {
            target = arguments.At(0);
            value = arguments.At(1);
        }

        if (!CommandTools.TryGetBots(target, out List<AudioPlayerBot> bots, out response))
            return false;

        bool toggle = value.Equals("toggle", StringComparison.OrdinalIgnoreCase);
        bool loop = false;
        if (!toggle && !CommandTools.TryParseBool(value, out loop))
        {
            response = "Loop value must be true, false, on, off, or toggle.";
            return false;
        }

        foreach (var bot in bots)
            bot.Loop = toggle ? !bot.Loop : loop;

        response = bots.Count == 1 ? $"Bot {bots[0].ID}: loop = {bots[0].Loop}." : $"Updated loop for {bots.Count} bot(s): {CommandTools.BotNames(bots)}.";
        return true;
    }
}