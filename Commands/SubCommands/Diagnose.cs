using CommandSystem;
using EviAudio.API;
using EviAudio.API.Spatial;
using Exiled.Permissions.Extensions;
using System;
using System.Text;

namespace EviAudio.Commands.SubCommands;

public class Diagnose : ICommand
{
    public string Command => "diagnose";
    public string[] Aliases => ["diag"];
    public string Description => "Check audio runtime dependencies and memory state.";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (!sender.CheckPermission($"audioplayer.{Command}"))
        {
            response = $"No permission: audioplayer.{Command}";
            return false;
        }

        var sb = new StringBuilder();
        sb.AppendLine("\n<b>EviAudio diagnostics</b>");
        sb.AppendLine($"  FFmpeg: {PcmDecoder.FindFfmpeg() ?? "not found"}");
        sb.AppendLine($"  yt-dlp: {PcmDecoder.FindYtDlp() ?? "not found"}");
        sb.AppendLine($"  Clip cache: {AudioClipCache.Count} item(s), {AudioClipCache.TotalBytes / 1024f / 1024f:F1} MB");
        sb.AppendLine($"  Controller IDs: {ControllerIdPool.UsedCount}/255");
        sb.AppendLine($"  Bots: {AudioController.GetAllAudioPlayers().Count}");
        sb.AppendLine($"  Spatial speakers: {SpatialAudioRegistry.All.Count}");

        response = sb.ToString();
        return true;
    }
}
