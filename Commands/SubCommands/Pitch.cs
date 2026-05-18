using CommandSystem;
using EviAudio.API.Container;
using Exiled.Permissions.Extensions;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace EviAudio.Commands.SubCommands;

public class Pitch : ICommand, IUsageProvider
{
    public string Command => "pitch";
    public string[] Aliases => ["pt", "semitones"];
    public string Description => "Set pitch shift in semitones for a bot, default bot, or all bots.";
    public string[] Usage => ["[Bot ID|default|all]", "Semitones (-24 to 24)"];

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (!sender.CheckPermission($"audioplayer.{Command}"))
        {
            response = $"No permission: audioplayer.{Command}";
            return false;
        }

        if (arguments.Count == 0)
        {
            response = "Usage: audio pitch [botId|default|all] <semitones -24..24>";
            return false;
        }

        string target;
        string value;

        if (arguments.Count == 1)
        {
            target = "default";
            value = arguments.At(0);
        }
        else
        {
            target = arguments.At(0);
            value = arguments.At(1);
        }

        if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float semitones) || semitones < -24f || semitones > 24f)
        {
            response = "Semitones must be a number between -24 and 24.";
            return false;
        }

        if (!CommandTools.TryGetBots(target, out List<AudioPlayerBot> bots, out response))
            return false;

        foreach (var bot in bots)
            bot.PitchShift = semitones;

        response = bots.Count == 1
            ? semitones == 0f ? $"Bot {bots[0].ID}: pitch reset to normal." : $"Bot {bots[0].ID}: pitch shift set to {semitones:+0.#;-0.#} semitones."
            : $"Set pitch to {semitones:+0.#;-0.#;0} semitones for {bots.Count} bot(s): {CommandTools.BotNames(bots)}.";
        return true;
    }
}