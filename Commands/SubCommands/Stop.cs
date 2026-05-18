using CommandSystem;
using EviAudio.API;
using EviAudio.API.Container;
using Exiled.Permissions.Extensions;
using System;
using System.Collections.Generic;

namespace EviAudio.Commands.SubCommands;

public class Stop : ICommand, IUsageProvider
{
    public string Command => "stop";
    public string[] Aliases => [];
    public string Description => "Stop audio playback on a bot, default bot, or all bots.";
    public string[] Usage => ["[Bot ID|default|all]"];

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (!sender.CheckPermission($"audioplayer.{Command}"))
        {
            response = $"No permission: audioplayer.{Command}";
            return false;
        }

        string target = arguments.Count == 0 ? "default" : arguments.At(0);
        if (!CommandTools.TryGetBots(target, out List<AudioPlayerBot> bots, out response))
            return false;

        foreach (var bot in bots)
            bot.StopAudio(clearQueue: true);

        response = bots.Count == 1 ? $"Bot {bots[0].ID}: stopped." : $"Stopped {bots.Count} bot(s): {CommandTools.BotNames(bots)}.";
        return true;
    }
}