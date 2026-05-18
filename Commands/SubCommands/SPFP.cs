using CommandSystem;
using EviAudio.API;
using EviAudio.API.Container;
using Exiled.API.Features;
using Exiled.Permissions.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EviAudio.Commands.SubCommands;

public class SPFP : ICommand, IUsageProvider
{
    public string Command => "stopplayfromplayers";
    public string[] Aliases => ["spfp", "stoppfp"];
    public string Description => "Remove players from personal playback, or stop personal playback for all selected targets.";
    public string[] Usage => ["Bot ID|default|all", "Player1.Player2|all"];

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (!sender.CheckPermission($"audioplayer.{Command}"))
        {
            response = $"No permission: audioplayer.{Command}";
            return false;
        }

        if (arguments.Count == 0)
        {
            response = "Usage: audio stopplayfromplayers <botId|default|all> <player1.player2|all>";
            return false;
        }

        if (arguments.Count == 1 && CommandTools.IsAllToken(arguments.At(0)))
        {
            var targeted = AudioController.GetAllAudioPlayers()
                .Where(bot => bot.BroadcastTo?.Count > 0)
                .ToList();

            foreach (var bot in targeted)
                bot.StopAudio(clearQueue: true);

            response = targeted.Count == 0 ? "No targeted playback found." : $"Stopped targeted playback on {targeted.Count} bot(s).";
            return true;
        }

        string botValue;
        string playersValue;

        if (arguments.Count == 1)
        {
            botValue = "default";
            playersValue = arguments.At(0);
        }
        else
        {
            botValue = arguments.At(0);
            playersValue = arguments.At(1);
        }

        if (!CommandTools.TryGetBots(botValue, out List<AudioPlayerBot> bots, out response))
            return false;

        if (CommandTools.IsAllToken(playersValue))
        {
            foreach (var bot in bots)
                bot.StopAudio(clearQueue: true);

            response = bots.Count == 1 ? $"Bot {bots[0].ID}: stopped personal playback for all targets." : $"Stopped personal playback on {bots.Count} bot(s): {CommandTools.BotNames(bots)}.";
            return true;
        }

        if (!CommandTools.TryGetPlayers(playersValue, out List<Player> players, out response, allowAll: false))
            return false;

        int changed = 0;
        foreach (var bot in bots)
        {
            bool botChanged = false;

            foreach (var player in players)
            {
                bool wasTargeted = bot.BroadcastTo != null && bot.BroadcastTo.Contains(player.Id);
                bot.RemovePlayerData(player.Id);
                if (!wasTargeted) continue;

                botChanged = true;
                changed++;
            }

            if (botChanged && bot.BroadcastTo?.Count == 0)
                bot.StopAudio(clearQueue: true);
        }

        response = changed == 0
            ? "No matching targeted playback was found."
            : $"Removed {players.Count} player(s) from targeted playback on {bots.Count} bot(s).";
        return changed > 0;
    }
}