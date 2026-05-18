using CommandSystem;
using EviAudio.API.Container;
using Exiled.Permissions.Extensions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace EviAudio.Commands.SubCommands;

public class Skip : ICommand, IUsageProvider
{
    public string Command => "skip";
    public string[] Aliases => ["next", "sk"];
    public string Description => "Skip the current track on a bot, default bot, or all playing bots.";
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

        var playing = bots.Where(bot => bot.IsPlaying).ToList();
        if (playing.Count == 0)
        {
            response = bots.Count == 1 ? $"Bot {bots[0].ID} is not playing anything." : "No selected bot is playing anything.";
            return false;
        }

        foreach (var bot in playing)
            bot.Skip();

        response = playing.Count == 1
            ? playing[0].IsPlaying ? $"Bot {playing[0].ID}: skipped, now playing '{Path.GetFileName(playing[0].CurrentTrack)}'." : $"Bot {playing[0].ID}: skipped, queue is empty."
            : $"Skipped current tracks on {playing.Count} bot(s): {CommandTools.BotNames(playing)}.";
        return true;
    }
}