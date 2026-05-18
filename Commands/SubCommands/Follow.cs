using CommandSystem;
using EviAudio.API;
using Exiled.API.Features;
using Exiled.Permissions.Extensions;
using System;

namespace EviAudio.Commands.SubCommands;

public class Follow : ICommand, IUsageProvider
{
    public string Command => "follow";
    public string[] Aliases => ["followplayer"];
    public string Description => "Make an audio bot follow a player.";
    public string[] Usage => ["Bot ID", "Player | stop", "Interval"];

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (!sender.CheckPermission($"audioplayer.{Command}"))
        {
            response = $"No permission: audioplayer.{Command}";
            return false;
        }

        if (arguments.Count < 2)
        {
            response = "Usage: audio follow <botId> <player|stop> [interval]";
            return false;
        }

        if (!int.TryParse(arguments.At(0), out int id))
        {
            response = "Bot ID must be a number.";
            return false;
        }

        var bot = AudioController.TryGetAudioPlayerContainer(id);
        if (bot == null)
        {
            response = $"Bot with ID {id} not found.";
            return false;
        }

        if (arguments.At(1).Equals("stop", StringComparison.OrdinalIgnoreCase))
        {
            bot.StopFollowing();
            response = $"Bot {id}: follow mode stopped.";
            return true;
        }

        Player player = Player.Get(arguments.At(1));
        if (player == null)
        {
            response = "Player not found.";
            return false;
        }

        float interval = 0.1f;
        if (arguments.Count > 2 && (!float.TryParse(arguments.At(2), out interval) || interval <= 0))
        {
            response = "Interval must be a positive number.";
            return false;
        }

        bot.FollowPlayer(player, interval);
        response = $"Bot {id}: following {player.Nickname}.";
        return true;
    }
}
