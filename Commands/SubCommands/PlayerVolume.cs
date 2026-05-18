using CommandSystem;
using EviAudio.API;
using Exiled.API.Features;
using Exiled.Permissions.Extensions;
using System;
using System.Collections.Generic;

namespace EviAudio.Commands.SubCommands;

public class PlayerVolume : ICommand, IUsageProvider
{
    public string Command => "playervolume";
    public string[] Aliases => ["pvol", "pv"];
    public string Description => "Set or reset personal playback volume for one or more listeners.";
    public string[] Usage => ["Bot ID|default", "Player1.Player2|all", "Volume (0-100)|reset"];

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (!sender.CheckPermission($"audioplayer.{Command}"))
        {
            response = $"No permission: audioplayer.{Command}";
            return false;
        }

        if (arguments.Count < 2)
        {
            response = "Usage: audio playervolume [botId|default] <player1.player2|all> <0-100|reset>";
            return false;
        }

        string botValue;
        string playersValue;
        string volumeValue;

        if (arguments.Count == 2)
        {
            botValue = "default";
            playersValue = arguments.At(0);
            volumeValue = arguments.At(1);
        }
        else
        {
            botValue = arguments.At(0);
            playersValue = arguments.At(1);
            volumeValue = arguments.At(2);
        }

        if (!CommandTools.TryGetBot(botValue, out var bot, out response))
            return false;

        if (!CommandTools.TryGetPlayers(playersValue, out List<Player> players, out response))
            return false;

        bool reset = volumeValue.Equals("reset", StringComparison.OrdinalIgnoreCase)
                     || volumeValue.Equals("clear", StringComparison.OrdinalIgnoreCase)
                     || volumeValue.Equals("default", StringComparison.OrdinalIgnoreCase);

        float volume = 100f;
        if (!reset && !CommandTools.TryParseVolume(volumeValue, out volume))
        {
            response = "Volume must be 0-100 or reset.";
            return false;
        }

        foreach (var player in players)
            bot.SetPlayerVolume(player.Id, reset ? 1f : volume * 0.01f);

        response = reset
            ? $"Bot {bot.ID}: reset personal volume for {players.Count} player(s)."
            : $"Bot {bot.ID}: personal volume set to {volume:F0}% for {players.Count} player(s).";
        return true;
    }
}