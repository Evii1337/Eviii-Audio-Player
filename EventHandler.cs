using EviAudio.API;
using EviAudio.API.Container;
using EviAudio.API.Preset;
using EviAudio.API.Spatial;
using EviAudio.Other;
using EviAudio.Other.DLC;
using Exiled.Events.EventArgs.Player;
using System.Collections.Generic;
using System.Linq;
using static EviAudio.Plugin;

namespace EviAudio;

internal sealed class EventHandler
{
    internal EventHandler()
    {
        Exiled.Events.Handlers.Player.Destroying += OnDestroying;
        Exiled.Events.Handlers.Player.Left += OnLeft;
        Exiled.Events.Handlers.Map.Generated += OnGenerated;
        Exiled.Events.Handlers.Server.RoundStarted += OnRoundStarted;
    }

    internal void Dispose()
    {
        Exiled.Events.Handlers.Player.Destroying -= OnDestroying;
        Exiled.Events.Handlers.Player.Left -= OnLeft;
        Exiled.Events.Handlers.Map.Generated -= OnGenerated;
        Exiled.Events.Handlers.Server.RoundStarted -= OnRoundStarted;
    }

    private static void OnDestroying(DestroyingEventArgs ev)
    {
        CleanupPlayerData(ev.Player?.Id ?? -1);

        foreach (KeyValuePair<int, AudioPlayerBot> kvp in AudioPlayerList)
        {
            if (ReferenceEquals(kvp.Value.Player, ev.Player))
            {
                kvp.Value.HandleExternalNpcDestroy();
                break;
            }
        }
    }

    private static void OnLeft(LeftEventArgs ev)
    {
        CleanupPlayerData(ev.Player?.Id ?? -1);
    }

    private static void OnGenerated()
    {
        SceneManager.DeactivateAll();
        ZoneManager?.CleanupAmbient();
        SpatialAudioRegistry.Clear();

        var bots = new List<AudioPlayerBot>(AudioPlayerList.Values);
        foreach (var bot in bots)
            bot.SafeDestroy();

        AudioPlayerList.Clear();
        ControllerIdPool.Clear();

        if (Instance.Config.SpecialEventsEnable && Plugin.LobbyEvents == null)
            Plugin.LobbyEvents = new LobbyEvents();

        if (!Instance.Config.SpawnBot) return;

        foreach (BotsList cfg in Instance.Config.BotsList)
            AudioPlayerBot.SpawnDummy(name: cfg.BotName, id: cfg.BotId);

        if (Instance.Config.EnableAudioZones && ZoneManager != null)
            ZoneManager.SpawnAmbientSpeakers();
    }

    private static void OnRoundStarted()
    {
        Plugin.RoundStartTime = System.DateTime.UtcNow;
    }

    private static void CleanupPlayerData(int playerId)
    {
        if (playerId < 0)
            return;

        foreach (var bot in AudioPlayerList.Values.ToList())
            bot.RemovePlayerData(playerId);

        foreach (var player in SpatialAudioRegistry.All.Values.ToList())
            player?.RemovePlayerData(playerId);
    }
}
