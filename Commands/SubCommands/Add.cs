using CommandSystem;
using EviAudio.API;
using EviAudio.API.Container;
using EviAudio.Other;
using Exiled.Permissions.Extensions;
using System;
using System.Globalization;
using System.Linq;

namespace EviAudio.Commands.SubCommands;

public class Add : ICommand, IUsageProvider
{
    public string Command => "add";
    public string[] Aliases => ["create", "cr", "fake", "bot"];
    public string Description => "Spawn an EviAudio bot. Bot ID can be omitted for the configured default.";
    public string[] Usage => ["[Bot ID|default]", "[Name]"];

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (!sender.CheckPermission($"audioplayer.{Command}"))
        {
            response = $"No permission: audioplayer.{Command}";
            return false;
        }

        int id = CommandTools.ResolveDefaultBotId();
        int nameStart = 0;

        if (arguments.Count > 0)
        {
            string first = arguments.At(0);
            if (int.TryParse(first, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedId))
            {
                id = parsedId;
                nameStart = 1;
            }
            else if (CommandTools.IsDefaultToken(first))
            {
                nameStart = 1;
            }
        }

        if (id.IsAudioPlayer())
        {
            response = $"Bot with ID {id} already exists.";
            return false;
        }

        BotsList cfg = AudioController.GetBotConfig(id);
        string name = cfg?.BotName ?? "EviAudio Bot";

        if (arguments.Count > nameStart)
            name = string.Join(" ", arguments.Skip(nameStart)).Replace('_', ' ');

        AudioPlayerBot.SpawnDummy(name: name, id: id);
        response = $"Spawned bot {id}: \"{name}\".";
        return true;
    }
}