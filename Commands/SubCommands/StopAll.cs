using CommandSystem;
using EviAudio.API;
using Exiled.API.Features;
using Exiled.Permissions.Extensions;
using System;
using System.Linq;

namespace EviAudio.Commands.SubCommands;

public class StopAll : ICommand, IUsageProvider
{
    public string Command => "stopall";
    public string[] Aliases => ["stopglobal", "sg", "sa"];
    public string Description => "Stop playback on all bots, or remove one player from targeted global playback.";
    public string[] Usage => ["[--target player]"];

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (!sender.CheckPermission($"audioplayer.{Command}"))
        {
            response = $"No permission: audioplayer.{Command}";
            return false;
        }

        if (arguments.Count > 0 && IsTargetFlag(arguments.At(0)))
        {
            if (arguments.Count < 2)
            {
                response = "Usage: audio stopall --target <player>";
                return false;
            }

            Player player = Player.Get(arguments.At(1));
            if (player == null)
            {
                response = $"Player '{arguments.At(1)}' not found.";
                return false;
            }

            int changed = 0;
            foreach (var bot in AudioController.GetAllAudioPlayers().ToList())
            {
                if (bot.BroadcastTo == null || !bot.BroadcastTo.Remove(player.Id))
                    continue;

                bot.RemovePlayerData(player.Id);
                changed++;

                if (bot.BroadcastTo.Count == 0)
                    bot.StopAudio(clearQueue: true);
            }

            response = changed == 0
                ? $"No targeted playback was found for {player.Nickname}."
                : $"Stopped targeted playback for {player.Nickname} on {changed} bot(s).";
            return true;
        }

        int count = 0;
        foreach (var bot in AudioController.GetAllAudioPlayers().ToList())
        {
            bot.StopAudio(clearQueue: true);
            count++;
        }

        response = count == 0 ? "No active bots to stop." : $"Stopped {count} bot(s).";
        return true;
    }

    private static bool IsTargetFlag(string value)
        => value.Equals("--target", StringComparison.OrdinalIgnoreCase)
           || value.Equals("target", StringComparison.OrdinalIgnoreCase)
           || value.Equals("-t", StringComparison.OrdinalIgnoreCase);
}
