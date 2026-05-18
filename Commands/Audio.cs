using CommandSystem;
using EviAudio.API;
using EviAudio.Commands.SubCommands;
using Exiled.Permissions.Extensions;
using System;
using System.Linq;
using System.Text;

namespace EviAudio.Commands;

[CommandHandler(typeof(RemoteAdminCommandHandler))]
public class AudioCommand : ParentCommand
{
    public AudioCommand() => LoadGeneratedCommands();

    public override string Command => "audiocommand";
    public override string[] Aliases => Plugin.Instance?.Config?.CommandName ?? ["audio", "au"];
    public override string Description => "EviAudio — spatial audio management.";

    public sealed override void LoadGeneratedCommands()
    {
        RegisterCommand(new Add());
        RegisterCommand(new Crossfade());
        RegisterCommand(new Diagnose());
        RegisterCommand(new Enqueue());
        RegisterCommand(new Fade());
        RegisterCommand(new Follow());
        RegisterCommand(new Folder());
        RegisterCommand(new Help(this));
        RegisterCommand(new Kick());
        RegisterCommand(new Loop());
        RegisterCommand(new Nickname());
        RegisterCommand(new Pause());
        RegisterCommand(new PFP());
        RegisterCommand(new PlayerVolume());
        RegisterCommand(new Pitch());
        RegisterCommand(new Play());
        RegisterCommand(new QueueCmd());
        RegisterCommand(new Scene());
        RegisterCommand(new Seek());
        RegisterCommand(new Skip());
        RegisterCommand(new Speaker());
        RegisterCommand(new SPFP());
        RegisterCommand(new Status());
        RegisterCommand(new Stop());
        RegisterCommand(new StopAll());
        RegisterCommand(new Visualize());
        RegisterCommand(new VoiceChannel());
        RegisterCommand(new Volume());
    }

    protected override bool ExecuteParent(
        ArraySegment<string> arguments,
        ICommandSender sender,
        out string response)
    {
        response = BuildHelp(sender);
        return false;
    }

    private const string C_HEADER = "#4FC3F7";
    private const string C_GROUP = "#80CBC4";
    private const string C_CMD = "#FFFFFF";
    private const string C_ALIAS = "#90A4AE";
    private const string C_ARG = "#FFE082";
    private const string C_DESC = "#B0BEC5";
    private const string C_DIM = "#546E7A";

    private static string Col(string color, string text) => $"<color={color}>{text}</color>";
    private static string Bold(string text) => $"<b>{text}</b>";

    private static readonly (string group, string[] cmds)[] Groups =
    [
        ("BOT MANAGEMENT", ["add", "kick", "status"]),
        ("PLAYBACK", ["play", "folder", "stop", "stopall", "pause", "skip", "seek", "crossfade", "loop", "volume", "fade", "pitch", "voicechannel"]),
        ("QUEUE", ["enqueue", "queue"]),
        ("TARGETING", ["playfromplayers", "stopplayfromplayers", "playervolume", "follow"]),
        ("SCENES", ["scene"]),
        ("SPATIAL SPEAKERS", ["speaker", "visualize"]),
        ("BOT APPEARANCE", ["nickname"]),
        ("DIAGNOSTICS", ["help", "diagnose"]),
    ];

    private string BuildHelp(ICommandSender sender)
    {
        var sb = new StringBuilder();
        string div = Col(C_DIM, "  ────────────────────────────────────────");
        sb.AppendLine();
        sb.AppendLine(Col(C_HEADER, Bold("  ╔══ EviAudio ═══════════════════════╗")));
        string version = Plugin.Instance?.Version?.ToString(3) ?? "unknown";
        sb.AppendLine(Col(C_HEADER, Bold("  ║")) + $"  {Bold("EviAudio")} {Col(C_DIM, $"v{version}")}   "
                      + Col("#A5D6A7", "tracks → Plugins/EviAudio/tracks/")
                      + "  " + Col(C_HEADER, Bold("║")));
        sb.AppendLine(Col(C_HEADER, Bold("  ╚══════════════════════════════════╝")));

        var cmdMap = AllCommands.ToDictionary(c => c.Command, c => c);

        foreach (var (group, names) in Groups)
        {
            var allowed = names
                .Where(cmdMap.ContainsKey)
                .Select(n => cmdMap[n])
                .ToList();

            if (allowed.Count == 0) continue;

            sb.AppendLine();
            sb.AppendLine($"  {Col(C_GROUP, Bold(group))}");
            sb.AppendLine(div);

            foreach (var cmd in allowed)
            {
                var usage = cmd is IUsageProvider up ? up.Usage : Array.Empty<string>();
                string args = usage.Length > 0
                    ? " " + string.Join(" ", usage.Select(u => Col(C_ARG, $"<{u}>")))
                    : string.Empty;

                string aliases = cmd.Aliases.Length > 0
                    ? "  " + Col(C_ALIAS, $"({string.Join(", ", cmd.Aliases)})")
                    : string.Empty;

                sb.AppendLine($"  {Col(C_CMD, Bold(cmd.Command))}{args}{aliases}");
                sb.AppendLine($"    {Col(C_DESC, cmd.Description)}");
            }
        }

        sb.AppendLine();
        sb.AppendLine(Col(C_DIM, "  Tracks folder: EXILED/Plugins/EviAudio/tracks/"));
        sb.AppendLine(Col(C_DIM, $"  Permissions prefix: {Plugin.Instance?.Config?.PermissionPrefix ?? "audioplayer"}.<command>"));

        return sb.ToString();
    }

    private sealed class Help : ICommand
    {
        private readonly AudioCommand _owner;

        public Help(AudioCommand owner)
        {
            _owner = owner;
        }

        public string Command => "help";
        public string[] Aliases => ["commands", "?"];
        public string Description => "Show all EviAudio commands.";

        public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
        {
            response = _owner.BuildHelp(sender);
            return true;
        }
    }
}
