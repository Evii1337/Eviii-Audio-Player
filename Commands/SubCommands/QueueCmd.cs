using CommandSystem;
using EviAudio.API;
using EviAudio.API.Container;
using Exiled.Permissions.Extensions;
using System;
using System.IO;
using System.Linq;
using System.Text;

namespace EviAudio.Commands.SubCommands;

public class QueueCmd : ICommand, IUsageProvider
{
    public string Command => "queue";
    public string[] Aliases => ["q", "playlist"];
    public string Description => "Show and edit a bot queue.";
    public string[] Usage => ["Bot ID | add/clear/shuffle/move"];

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (!sender.CheckPermission($"audioplayer.{Command}"))
        {
            response = $"No permission: audioplayer.{Command}";
            return false;
        }

        if (arguments.Count == 0)
        {
            response = "Usage: audio queue <botId> | add/next/remove/removeby/clear/shuffle/move <botId> ...";
            return false;
        }

        string sub = arguments.At(0).ToLowerInvariant();
        return sub switch
        {
            "add" => Add(arguments, out response),
            "next" or "insertnext" => InsertNext(arguments, out response),
            "remove" or "del" => Remove(arguments, out response),
            "removeby" or "removebypath" => RemoveByPath(arguments, out response),
            "clear" => Clear(arguments, out response),
            "shuffle" => Shuffle(arguments, out response),
            "move" => Move(arguments, out response),
            _ => Show(arguments, out response),
        };
    }

    private static bool InsertNext(ArraySegment<string> arguments, out string response)
    {
        if (arguments.Count < 3)
        {
            response = "Usage: audio queue next <botId> <path>";
            return false;
        }

        if (!TryGetBot(arguments.At(1), out var bot, out response))
            return false;

        string path = string.Join(" ", arguments.Skip(2));
        bot.InsertNext(path);
        response = $"Bot {bot.ID}: inserted next '{path}'.";
        return true;
    }

    private static bool Remove(ArraySegment<string> arguments, out string response)
    {
        if (arguments.Count < 3)
        {
            response = "Usage: audio queue remove <botId> <index>";
            return false;
        }

        if (!TryGetBot(arguments.At(1), out var bot, out response))
            return false;

        if (!int.TryParse(arguments.At(2), out int index))
        {
            response = "Index must be a number.";
            return false;
        }

        if (!bot.RemoveQueueAt(index - 1))
        {
            response = "Queue index is out of range.";
            return false;
        }

        response = $"Bot {bot.ID}: removed queue item {index}.";
        return true;
    }

    private static bool RemoveByPath(ArraySegment<string> arguments, out string response)
    {
        if (arguments.Count < 3)
        {
            response = "Usage: audio queue removeby <botId> <path|filename>";
            return false;
        }

        if (!TryGetBot(arguments.At(1), out var bot, out response))
            return false;

        string path = string.Join(" ", arguments.Skip(2));
        int removed = bot.RemoveQueueByPath(path);
        response = $"Bot {bot.ID}: removed {removed} matching queue item(s).";
        return true;
    }

    private static bool Show(ArraySegment<string> arguments, out string response)
    {
        if (!TryGetBot(arguments.At(0), out var bot, out response))
            return false;

        var sb = new StringBuilder();
        sb.AppendLine($"\n<b>Bot {bot.ID} — Queue</b>");

        if (bot.IsPlaying)
            sb.AppendLine($"  <b>▶ NOW:</b> {Path.GetFileName(bot.CurrentTrack)}  {Format(bot.Position)} / {Format(bot.Duration)}");

        var queue = bot.GetQueue();
        if (queue.Count == 0)
        {
            sb.AppendLine("  (queue is empty)");
        }
        else
        {
            int show = Math.Min(queue.Count, 20);
            for (int i = 0; i < show; i++)
                sb.AppendLine($"  {i + 1}. {Path.GetFileName(queue[i])}");

            if (queue.Count > show)
                sb.AppendLine($"  ... and {queue.Count - show} more.");
        }

        response = sb.ToString();
        return true;
    }

    private static bool Add(ArraySegment<string> arguments, out string response)
    {
        if (arguments.Count < 3)
        {
            response = "Usage: audio queue add <botId> <path>";
            return false;
        }

        if (!TryGetBot(arguments.At(1), out var bot, out response))
            return false;

        string path = string.Join(" ", arguments.Skip(2));
        bot.Enqueue(path);
        response = $"Bot {bot.ID}: queued '{path}'.";
        return true;
    }

    private static bool Clear(ArraySegment<string> arguments, out string response)
    {
        if (arguments.Count < 2)
        {
            response = "Usage: audio queue clear <botId>";
            return false;
        }

        if (!TryGetBot(arguments.At(1), out var bot, out response))
            return false;

        bot.ClearQueue();
        response = $"Bot {bot.ID}: queue cleared.";
        return true;
    }

    private static bool Shuffle(ArraySegment<string> arguments, out string response)
    {
        if (arguments.Count < 2)
        {
            response = "Usage: audio queue shuffle <botId>";
            return false;
        }

        if (!TryGetBot(arguments.At(1), out var bot, out response))
            return false;

        bot.ShuffleQueue();
        response = $"Bot {bot.ID}: queue shuffled.";
        return true;
    }

    private static bool Move(ArraySegment<string> arguments, out string response)
    {
        if (arguments.Count < 4)
        {
            response = "Usage: audio queue move <botId> <from> <to>";
            return false;
        }

        if (!TryGetBot(arguments.At(1), out var bot, out response))
            return false;

        if (!int.TryParse(arguments.At(2), out int from) || !int.TryParse(arguments.At(3), out int to))
        {
            response = "from/to must be numbers.";
            return false;
        }

        if (!bot.MoveQueueItem(from - 1, to - 1))
        {
            response = "Queue positions are out of range.";
            return false;
        }

        response = $"Bot {bot.ID}: moved queue item {from} to {to}.";
        return true;
    }

    private static bool TryGetBot(string value, out AudioPlayerBot bot, out string response)
        => CommandTools.TryGetBot(value, out bot, out response);

    private static string Format(TimeSpan time)
    {
        if (time <= TimeSpan.Zero)
            return "--:--";

        return time.TotalHours >= 1
            ? time.ToString(@"h\:mm\:ss")
            : time.ToString(@"m\:ss");
    }
}
