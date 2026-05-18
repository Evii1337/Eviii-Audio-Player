using CommandSystem;
using EviAudio.API.Container;
using Exiled.Permissions.Extensions;
using System;
using System.Collections.Generic;
using VoiceChat;

namespace EviAudio.Commands.SubCommands;

public class VoiceChannel : ICommand, IUsageProvider
{
    public string Command => "voicechannel";
    public string[] Aliases => ["voice", "channel", "chan", "audiochannel"];
    public string Description => "Change the voice channel for a bot, default bot, or all bots.";
    public string[] Usage => ["[Bot ID|default|all]", "VoiceChatChannel"];

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (!sender.CheckPermission($"audioplayer.{Command}"))
        {
            response = $"No permission: audioplayer.{Command}";
            return false;
        }

        if (arguments.Count == 0)
        {
            response = "Usage: audio voicechannel [botId|default|all] <VoiceChatChannel>";
            return false;
        }

        string target;
        string channelValue;

        if (arguments.Count == 1)
        {
            target = "default";
            channelValue = arguments.At(0);
        }
        else
        {
            target = arguments.At(0);
            channelValue = arguments.At(1);
        }

        if (!Enum.TryParse(channelValue, true, out VoiceChatChannel channel))
        {
            response = $"Unknown VoiceChatChannel: {channelValue}. Valid values: {string.Join(", ", Enum.GetNames(typeof(VoiceChatChannel)))}";
            return false;
        }

        if (!CommandTools.TryGetBots(target, out List<AudioPlayerBot> bots, out response))
            return false;

        foreach (var bot in bots)
            bot.VoiceChatChannel = channel;

        response = bots.Count == 1 ? $"Bot {bots[0].ID}: channel set to {channel}." : $"Set channel to {channel} for {bots.Count} bot(s): {CommandTools.BotNames(bots)}.";
        return true;
    }
}