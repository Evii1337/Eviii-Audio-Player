using EviAudio.API;
using EviAudio.API.Container;
using Exiled.API.Features;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace EviAudio.Commands;

internal static class CommandTools
{
    private static readonly char[] PlayerSeparators = ['.', ',', ';'];

    public static int ResolveDefaultBotId()
    {
        int configured = Plugin.Instance?.Config?.DefaultBotId ?? 99;

        if (AudioController.TryGetAudioPlayerContainer(configured) != null)
            return configured;

        var bots = AudioController.GetAllAudioPlayers();
        return bots.Count == 1 ? bots.First().ID : configured;
    }

    public static bool IsDefaultToken(string value)
        => value.Equals("default", StringComparison.OrdinalIgnoreCase)
           || value.Equals("main", StringComparison.OrdinalIgnoreCase)
           || value.Equals("auto", StringComparison.OrdinalIgnoreCase);

    public static bool IsAllToken(string value)
        => value.Equals("all", StringComparison.OrdinalIgnoreCase)
           || value.Equals("*", StringComparison.OrdinalIgnoreCase);

    public static bool IsFlag(string value, params string[] names)
        => names.Any(name => value.Equals(name, StringComparison.OrdinalIgnoreCase));

    public static bool TryGetBot(string value, out AudioPlayerBot bot, out string response)
    {
        bot = null;

        int id;
        if (IsDefaultToken(value))
        {
            id = ResolveDefaultBotId();
        }
        else if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out id))
        {
            response = "Bot ID must be a number, default, or all.";
            return false;
        }

        bot = AudioController.TryGetAudioPlayerContainer(id);
        if (bot == null)
        {
            response = $"Bot with ID {id} not found.";
            return false;
        }

        response = null;
        return true;
    }

    public static bool TryGetBots(string value, out List<AudioPlayerBot> bots, out string response)
    {
        bots = null;

        if (IsAllToken(value))
        {
            bots = AudioController.GetAllAudioPlayers().ToList();
            if (bots.Count == 0)
            {
                response = "No active bots found.";
                return false;
            }

            response = null;
            return true;
        }

        if (!TryGetBot(value, out var bot, out response))
            return false;

        bots = [bot];
        return true;
    }

    public static List<Player> GetPlayablePlayers()
        => Player.List
            .Where(player => player != null && player.IsConnected && !player.IsAudioPlayer())
            .GroupBy(player => player.Id)
            .Select(group => group.First())
            .ToList();

    public static bool TryGetPlayers(string value, out List<Player> players, out string response, bool allowAll = true)
    {
        players = null;
        response = null;

        if (allowAll && IsAllToken(value))
        {
            players = GetPlayablePlayers();
            return true;
        }

        var found = new List<Player>();
        var missing = new List<string>();
        string[] tokens = value.Split(PlayerSeparators, StringSplitOptions.RemoveEmptyEntries);

        if (tokens.Length == 0)
        {
            response = "Player value is empty.";
            return false;
        }

        foreach (string raw in tokens)
        {
            string token = raw.Trim();
            if (token.Length == 0)
                continue;

            Player player = Player.Get(token);
            if (player == null || player.IsAudioPlayer())
            {
                missing.Add(token);
                continue;
            }

            if (found.All(existing => existing.Id != player.Id))
                found.Add(player);
        }

        if (missing.Count > 0)
        {
            response = $"Player(s) not found: {string.Join(", ", missing)}.";
            return false;
        }

        if (found.Count == 0)
        {
            response = "No players found.";
            return false;
        }

        players = found;
        return true;
    }

    public static bool TryParseVolume(string value, out float volume)
        => float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out volume)
           && volume >= 0f
           && volume <= 100f;

    public static bool TryParseBool(string value, out bool result)
    {
        if (bool.TryParse(value, out result))
            return true;

        if (value.Equals("on", StringComparison.OrdinalIgnoreCase) || value.Equals("yes", StringComparison.OrdinalIgnoreCase) || value.Equals("1", StringComparison.OrdinalIgnoreCase))
        {
            result = true;
            return true;
        }

        if (value.Equals("off", StringComparison.OrdinalIgnoreCase) || value.Equals("no", StringComparison.OrdinalIgnoreCase) || value.Equals("0", StringComparison.OrdinalIgnoreCase))
        {
            result = false;
            return true;
        }

        return false;
    }

    public static string Join(ArraySegment<string> arguments, int start)
        => string.Join(" ", arguments.Skip(start));

    public static string BotNames(IEnumerable<AudioPlayerBot> bots)
        => string.Join(", ", bots.Select(bot => bot.ID));
}