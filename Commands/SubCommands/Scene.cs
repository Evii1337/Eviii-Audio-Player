using CommandSystem;
using EviAudio.API.Preset;
using Exiled.Permissions.Extensions;
using System;
using System.Linq;
using System.Text;

namespace EviAudio.Commands.SubCommands;

public class Scene : ICommand, IUsageProvider
{
    public string Command => "scene";
    public string[] Aliases => ["preset", "setup"];
    public string Description => "Activate, deactivate, or list audio scene presets.";
    public string[] Usage => ["activate|deactivate|stop|list", "Preset Name (for activate/deactivate)"];

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (!sender.CheckPermission($"audioplayer.{Command}"))
        {
            response = $"No permission: audioplayer.{Command}";
            return false;
        }

        if (arguments.Count == 0)
        {
            response = "Usage: audio scene <activate|deactivate|list> [preset name]";
            return false;
        }

        string sub = arguments.At(0).ToLowerInvariant();

        switch (sub)
        {
            case "list":
                return ListScenes(out response);

            case "activate":
            case "start":
                if (arguments.Count < 2)
                {
                    response = "Usage: audio scene activate <Preset Name>";
                    return false;
                }
                return ActivateScene(string.Join(" ", arguments.Skip(1)), out response);

            case "deactivate":
            case "stop":
                if (arguments.Count < 2)
                {
                    response = "Usage: audio scene deactivate <Preset Name>";
                    return false;
                }
                return DeactivateScene(string.Join(" ", arguments.Skip(1)), out response);

            case "stopall":
                SceneManager.DeactivateAll();
                response = "All active scenes deactivated.";
                return true;

            default:
                response = $"Unknown sub-command '{sub}'. Use: activate, deactivate, list, stopall.";
                return false;
        }
    }

    private static bool ActivateScene(string name, out string response)
    {
        var (success, message, players) = SceneManager.ActivateScene(name);
        response = message;
        return success;
    }

    private static bool DeactivateScene(string name, out string response)
    {
        var (success, message) = SceneManager.DeactivateScene(name);
        response = message;
        return success;
    }

    private static bool ListScenes(out string response)
    {
        var presets = Plugin.Instance?.Config?.AudioPresets;
        var active = SceneManager.ActiveScenes;

        if (presets == null || presets.Count == 0)
        {
            response = "No audio presets defined in config.";
            return true;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"\n<b>Audio Presets ({presets.Count} defined, {active.Count} active)</b>\n");

        foreach (var p in presets)
        {
            bool isActive = active.ContainsKey(p.Name);
            int count = isActive ? active[p.Name].Count : 0;
            string status = isActive ? $"<color=green>ACTIVE ({count} speakers)</color>":"<color=grey>idle</color>";
            sb.AppendLine($" {p.Name} — {p.Speakers?.Count ?? 0} speaker config(s) [{status}]");
        }
        response = sb.ToString();
        return true;
    }
}
