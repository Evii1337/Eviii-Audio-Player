using CommandSystem;
using EviAudio.API;
using EviAudio.Other;
using Exiled.Permissions.Extensions;
using System;

namespace EviAudio.Commands.SubCommands;

public class Kick : ICommand, IUsageProvider
{
    public string Command => "kick";
    public string[] Aliases => ["delete", "del", "remove", "rem", "destroy"];
    public string Description => "Destroy an EviAudio bot.";
    public string[] Usage => ["Bot ID"];

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (!sender.CheckPermission($"audioplayer.{Command}"))
        {
            response = $"No permission: audioplayer.{Command}";
            return false;
        }

        if (arguments.Count == 0)
        {
            response = "Usage: audio kick {Bot ID}";
            return false;
        }

        if (!int.TryParse(arguments.At(0), out int id))
        {
            response = "Bot ID must be a number.";
            return false;
        }

        if (AudioController.TryGetAudioPlayerContainer(id) == null)
        {
            response = $"Bot with ID {id} not found.";
            return false;
        }

        AudioController.DisconnectDummy(id);
        response = $"Bot {id} destroyed.";
        return true;
    }
}
