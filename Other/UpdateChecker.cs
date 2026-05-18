using Exiled.API.Features;
using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace EviAudio.Other;

internal static class UpdateChecker
{
    private const string ReleasesApi = "https://api.github.com/repos/Evii1337/Eviii-Audio-Player/releases/latest";
    private const string ReleasesUrl = "https://github.com/Evii1337/Eviii-Audio-Player/releases";

    internal static async Task CheckAsync(Version current)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            client.DefaultRequestHeaders.Add("User-Agent", "EviAudio-UpdateChecker");

            string json = await client.GetStringAsync(ReleasesApi);
            Match match = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"v?([\\d.]+)\"");

            if (!match.Success)
                return;

            if (!Version.TryParse(match.Groups[1].Value, out Version latest))
                return;

            if (latest <= current)
            {
                Log.Debug($"You are running the latest version ({current.ToString(3)}).");
                return;
            }

            Log.Warn("EviAudio update available.");
            Log.Warn($"Current version: {current.ToString(3)}");
            Log.Warn($"Latest version: {latest.ToString(3)}");
            Log.Warn(ReleasesUrl);
        }
        catch (Exception ex)
        {
            Log.Debug($"Update check failed: {ex.Message}");
        }
    }
}
