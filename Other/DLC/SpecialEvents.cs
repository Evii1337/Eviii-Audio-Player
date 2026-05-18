using Exiled.Events.EventArgs.Map;
using Exiled.Events.EventArgs.Player;
using Exiled.Events.EventArgs.Server;
using PlayerRoles;
using static EviAudio.Plugin;

namespace EviAudio.Other.DLC;

internal sealed class SpecialEvents
{
    public SpecialEvents()
    {
        Exiled.Events.Handlers.Player.Died += OnDied;
        Exiled.Events.Handlers.Player.Verified += OnVerified;
        Exiled.Events.Handlers.Server.RoundEnded += OnRoundEnded;
        Exiled.Events.Handlers.Server.RoundStarted += OnRoundStarted;
        Exiled.Events.Handlers.Server.RespawningTeam += OnRespawningTeam;
        Exiled.Events.Handlers.Map.AnnouncingNtfEntrance += OnAnnouncingNtfEntrance;
    }

    public void Dispose()
    {
        Exiled.Events.Handlers.Player.Died -= OnDied;
        Exiled.Events.Handlers.Player.Verified -= OnVerified;
        Exiled.Events.Handlers.Server.RoundEnded -= OnRoundEnded;
        Exiled.Events.Handlers.Server.RoundStarted -= OnRoundStarted;
        Exiled.Events.Handlers.Server.RespawningTeam -= OnRespawningTeam;
        Exiled.Events.Handlers.Map.AnnouncingNtfEntrance -= OnAnnouncingNtfEntrance;
    }

    private static void OnRoundStarted()
        => Extensions.PlayRandomAudioFile(Instance.Config.RoundStartClip, "RoundStartClip");

    private static void OnRoundEnded(RoundEndedEventArgs ev)
        => Extensions.PlayRandomAudioFile(Instance.Config.RoundEndClip, "RoundEndClip");

    private static void OnVerified(VerifiedEventArgs ev)
        => Extensions.PlayRandomAudioFileFromPlayer(Instance.Config.PlayerConnectedServer, ev.Player, "PlayerConnectedServer");

    private static void OnAnnouncingNtfEntrance(AnnouncingNtfEntranceEventArgs ev)
    {
        if (Instance.Config.SuppressCassieMtfAnnouncement)
            ev.IsAllowed = false;
    }

    private static void OnDied(DiedEventArgs ev)
    {
        if (ev.Player == null || ev.Attacker == null || ev.DamageHandler.Type == Exiled.API.Enums.DamageType.Unknown)
            return;

        Extensions.PlayRandomAudioFileFromPlayer(Instance.Config.PlayerDiedTargetClip, ev.Player, "PlayerDiedTargetClip");
        Extensions.PlayRandomAudioFileFromPlayer(Instance.Config.PlayerDiedKillerClip, ev.Attacker, "PlayerDiedKillerClip");
    }

    private static void OnRespawningTeam(RespawningTeamEventArgs ev)
    {
        if (ev.NextKnownTeam == Faction.FoundationStaff)
        {
            if (Instance.Config.MtfSpawnEnabled)
                Extensions.PlayRandomAudioFile(Instance.Config.MtfSpawnClip, "MtfSpawnClip");
        }
        else
        {
            if (Instance.Config.ChaosSpawnEnabled)
                Extensions.PlayRandomAudioFile(Instance.Config.ChaosSpawnClip, "ChaosSpawnClip");
        }
    }
}
