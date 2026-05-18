using CommandSystem;
using Exiled.Permissions.Extensions;
using System;
using System.Globalization;

namespace EviAudio.Commands.SubCommands;

public class Seek : ICommand, IUsageProvider
{
    public string Command => "seek";
    public string[] Aliases => ["pos"];
    public string Description => "Seek a bot track to seconds or a time value. Bot ID can be omitted for the default bot.";
    public string[] Usage => ["[Bot ID|default]", "Seconds|m:ss"];

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (!sender.CheckPermission($"audioplayer.{Command}"))
        {
            response = $"No permission: audioplayer.{Command}";
            return false;
        }

        if (arguments.Count == 0)
        {
            response = "Usage: audio seek [botId|default] <seconds|m:ss>";
            return false;
        }

        string botValue;
        string timeValue;

        if (arguments.Count == 1)
        {
            botValue = "default";
            timeValue = arguments.At(0);
        }
        else
        {
            botValue = arguments.At(0);
            timeValue = arguments.At(1);
        }

        if (!TryParseTime(timeValue, out TimeSpan position))
        {
            response = "Position must be a positive number of seconds or a time like 1:23.";
            return false;
        }

        if (!CommandTools.TryGetBot(botValue, out var bot, out response))
            return false;

        if (!bot.SeekTo(position))
        {
            response = "Seek failed. The track may not be loaded yet or FFmpeg could not reopen the stream.";
            return false;
        }

        response = $"Bot {bot.ID}: seeked to {Format(position)}.";
        return true;
    }

    private static bool TryParseTime(string value, out TimeSpan time)
    {
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds) && seconds >= 0)
        {
            time = TimeSpan.FromSeconds(seconds);
            return true;
        }

        if (TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out time) && time >= TimeSpan.Zero)
            return true;

        time = TimeSpan.Zero;
        return false;
    }

    private static string Format(TimeSpan time)
        => time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");
}