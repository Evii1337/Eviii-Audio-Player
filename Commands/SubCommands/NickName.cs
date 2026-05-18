using CommandSystem;
using EviAudio.API;
using Exiled.Permissions.Extensions;
using System;
using System.Linq;

namespace EviAudio.Commands.SubCommands;

public class Nickname : ICommand, IUsageProvider
{
    public string Command => "nickname";
    public string[] Aliases => ["setnickname", "setnick", "nick", "name"];
    public string Description => "Set the display name of a bot.";
    public string[] Usage => ["Bot ID", "Name"];

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (!sender.CheckPermission($"audioplayer.{Command}"))
        {
            response = $"No permission: audioplayer.{Command}";
            return false;
        }

        if (arguments.Count < 2)
        {
            response = "Usage: audio nickname {Bot ID} {Name}";
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

        string nickname = string.Join(" ", arguments.Skip(1));
        bot.SetNickname(nickname);
        response = $"Bot {id} renamed to '{nickname}'.";
        return true;
    }
}
