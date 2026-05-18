using CommandSystem;
using EviAudio.API.Spatial;
using Exiled.Permissions.Extensions;
using System;

namespace EviAudio.Commands.SubCommands;

public class Visualize : ICommand, IUsageProvider
{
    public string Command => "visualize";
    public string[] Aliases => ["viz", "vis", "showspeakers"];
    public string Description => "Display speaker radius spheres for active SpatialAudioPlayers.";
    public string[] Usage => ["[Speaker Registry ID | all]", "[Duration seconds]"];

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (!sender.CheckPermission($"audioplayer.{Command}"))
        {
            response = $"No permission: audioplayer.{Command}";
            return false;
        }

        float duration = Plugin.Instance?.Config?.VisualizerDuration ?? 5f;

        if (arguments.Count >= 2 && float.TryParse(arguments.At(1), out float d) && d > 0)
            duration = d;

        if (arguments.Count == 0 || arguments.At(0).Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            int count = SpatialAudioRegistry.All.Count;
            if (count == 0)
            {
                response = "No active SpatialAudioPlayers to visualize.";
                return true;
            }
            SpeakerVisualizer.ShowAll(duration);
            response = $"Visualizing {count} speaker(s) for {duration:F1}s.";
            return true;
        }

        if (!int.TryParse(arguments.At(0), out int regId))
        {
            response = "Argument must be a speaker registry ID or 'all'.";
            return false;
        }

        var player = SpatialAudioRegistry.Get(regId);
        if (player?.Speaker == null)
        {
            response = $"No speaker found with registry ID {regId}.";
            return false;
        }

        SpeakerVisualizer.Show(
            player.Speaker.transform.position,
            player.Speaker.NetworkMinDistance,
            player.Speaker.NetworkMaxDistance,
            duration);

        response = $"Visualizing speaker #{regId} for {duration:F1}s.";
        return true;
    }
}
