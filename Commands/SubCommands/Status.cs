using CommandSystem;
using EviAudio.API;
using Exiled.Permissions.Extensions;
using System;
using System.IO;
using System.Text;

namespace EviAudio.Commands.SubCommands;

public class Status : ICommand
{
    public string Command => "status";
    public string[] Aliases => ["info", "list", "bots"];
    public string Description => "Show status of all active audio bots.";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (!sender.CheckPermission($"audioplayer.{Command}"))
        {
            response = $"No permission: audioplayer.{Command}";
            return false;
        }

        var bots = AudioController.GetAllAudioPlayers();

        if (bots.Count == 0)
        {
            response = "No audio bots are currently spawned.";
            return true;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"\n<b>EviAudio — {bots.Count} bot(s) active</b>\n");

        foreach (var bot in bots)
        {
            string state = bot.IsPlaying
                ? bot.IsPaused ? "⏸ PAUSED" : "▶ PLAYING"
                : "⏹ IDLE";

            sb.AppendLine($"  <b>Bot {bot.ID}</b>  \"{bot.Name}\"  [{state}]");
            sb.AppendLine($"    Channel: {bot.VoiceChatChannel}  Volume: {bot.Volume:F0}%  Loop: {bot.Loop}  Pitch: {bot.PitchShift:+0.#;-0.#;0} st");
            sb.AppendLine($"    Position: {Format(bot.Position)} / {Format(bot.Duration)}  Listeners: {bot.GetListenerCount()}");

            if (bot.IsPlaying)
            {
                string fallback = Path.GetFileName(bot.CurrentTrack);
                sb.AppendLine($"    Track: {bot.CurrentMetadata.DisplayName(fallback)}");
            }

            var queue = bot.GetQueue();
            if (queue.Count > 0)
                sb.AppendLine($"    Queue: {queue.Count} track(s) — next: {Path.GetFileName(queue[0])}");

            if (bot.BroadcastTo?.Count > 0)
                sb.AppendLine($"    Targets: [{string.Join(", ", bot.BroadcastTo)}]");

            if (bot.PlayerVolumes?.Count > 0)
                sb.AppendLine($"    Personal volumes: {bot.PlayerVolumes.Count}");
        }

        sb.AppendLine();
        sb.AppendLine($"  Cache: {AudioClipCache.Count} clip(s), {AudioClipCache.TotalBytes / 1024f / 1024f:F1} MB  Controller IDs: {ControllerIdPool.UsedCount}/255");

        response = sb.ToString();
        return true;
    }

    private static string Format(TimeSpan time)
    {
        if (time <= TimeSpan.Zero)
            return "--:--";

        return time.TotalHours >= 1
            ? time.ToString(@"h\:mm\:ss")
            : time.ToString(@"m\:ss");
    }
}
