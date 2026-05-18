using EviAudio.API.Container;
using EviAudio.Other;
using Exiled.API.Features;
using PlayerRoles;
using System;
using System.Collections.Generic;

namespace EviAudio.API;

public static class AudioController
{
    public static AudioPlayerBot SpawnDummy(
        int id = 99,
        string name = "Dedicated Server",
        RoleTypeId roleTypeId = RoleTypeId.Tutorial,
        bool ignored = true)
        => AudioPlayerBot.SpawnDummy(name, id, roleTypeId, ignored);

    public static void DisconnectDummy(int id = 99)
        => TryGetAudioPlayerContainer(id)?.SafeDestroy();

    internal static AudioPlayerBot GetOrSpawnAudioPlayerContainer(int id)
    {
        var existing = TryGetAudioPlayerContainer(id);
        if (existing != null)
            return existing;

        BotsList cfg = GetBotConfig(id);
        string name = cfg?.BotName ?? "EviAudio Bot";
        return SpawnDummy(id, name);
    }

    public static AudioPlayerBot TryGetAudioPlayerContainer(int id)
    {
        Plugin.AudioPlayerList.TryGetValue(id, out var bot);
        return bot;
    }

    public static bool IsAudioPlayer(this int botId) => TryGetAudioPlayerContainer(botId) != null;

    public static bool IsAudioPlayer(this Player player)
    {
        if (player == null)
            return false;

        foreach (var bot in Plugin.AudioPlayerList.Values)
            if (bot.Player != null && ReferenceEquals(bot.Player, player))
                return true;

        return false;
    }

    public static ICollection<AudioPlayerBot> GetAllAudioPlayers() => Plugin.AudioPlayerList.Values;

    public static BotsList GetBotConfig(int id)
    {
        if (Plugin.Instance?.Config?.BotsList == null) return null;
        foreach (var cfg in Plugin.Instance.Config.BotsList)
            if (cfg.BotId == id) return cfg;
        return null;
    }

    public static BotsList GetBotConfig(string name)
    {
        if (Plugin.Instance?.Config?.BotsList == null) return null;
        foreach (var cfg in Plugin.Instance.Config.BotsList)
            if (string.Equals(cfg.BotName, name, StringComparison.OrdinalIgnoreCase)) return cfg;
        return null;
    }
}