using CommandSystem;
using EviAudio.API.Container;
using Exiled.Permissions.Extensions;
using System;
using System.Collections.Generic;

namespace EviAudio.Commands.SubCommands;

public class Pause : ICommand, IUsageProvider
{
    public string Command => "pause";
    public string[] Aliases => ["resume", "togglepause"];
    public string Description => "Pause, resume, or toggle playback for a bot, default bot, or all bots.";
    public string[] Usage => ["[Bot ID|default|all]", "[on|off|toggle]"];

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (!sender.CheckPermission($"audioplayer.{Command}"))
        {
            response = $"No permission: audioplayer.{Command}";
            return false;
        }

        string target = "default";
        string mode = "toggle";

        if (arguments.Count == 1)
        {
            if (IsMode(arguments.At(0)))
                mode = arguments.At(0);
            else
                target = arguments.At(0);
        }
        else if (arguments.Count >= 2)
        {
            target = arguments.At(0);
            mode = arguments.At(1);
        }

        if (!CommandTools.TryGetBots(target, out List<AudioPlayerBot> bots, out response))
            return false;

        foreach (var bot in bots)
        {
            if (mode.Equals("toggle", StringComparison.OrdinalIgnoreCase))
                bot.IsPaused = !bot.IsPaused;
            else if (IsPauseMode(mode))
                bot.IsPaused = true;
            else if (IsResumeMode(mode))
                bot.IsPaused = false;
            else
            {
                response = "Mode must be on, off, pause, resume, or toggle.";
                return false;
            }
        }

        response = bots.Count == 1
            ? (bots[0].IsPaused ? $"Bot {bots[0].ID}: paused." : $"Bot {bots[0].ID}: resumed.")
            : $"Updated pause state for {bots.Count} bot(s): {CommandTools.BotNames(bots)}.";
        return true;
    }

    private static bool IsMode(string value)
        => value.Equals("toggle", StringComparison.OrdinalIgnoreCase) || IsPauseMode(value) || IsResumeMode(value);

    private static bool IsPauseMode(string value)
        => value.Equals("on", StringComparison.OrdinalIgnoreCase)
           || value.Equals("pause", StringComparison.OrdinalIgnoreCase)
           || value.Equals("paused", StringComparison.OrdinalIgnoreCase)
           || value.Equals("true", StringComparison.OrdinalIgnoreCase);

    private static bool IsResumeMode(string value)
        => value.Equals("off", StringComparison.OrdinalIgnoreCase)
           || value.Equals("resume", StringComparison.OrdinalIgnoreCase)
           || value.Equals("resumed", StringComparison.OrdinalIgnoreCase)
           || value.Equals("false", StringComparison.OrdinalIgnoreCase);
}