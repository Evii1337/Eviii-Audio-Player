using Exiled.API.Features;
using System.Collections.Generic;
using System.IO;
using static EviAudio.Plugin;

namespace EviAudio.Other;

public static class Extensions
{
    public static readonly AudioFile EmptyClip = new(string.Empty, botId: -1);

    internal static void CreateDirectory()
    {
        string tracksPath = Instance.AudioPath;
        if (!Directory.Exists(tracksPath))
        {
            Directory.CreateDirectory(tracksPath);
            Log.Info($"Created tracks folder: {tracksPath}");
        }
    }

    public static AudioFile GetRandomAudioClip(List<AudioFile> audioClips, string listName)
    {
        if (audioClips == null || audioClips.Count == 0)
        {
            if (Instance.Config.Debug)
                Log.Warn($"{listName} is null or empty.");
            return EmptyClip;
        }

        AudioFile selected = null;
        int count = 0;

        foreach (var clip in audioClips)
        {
            if (!AudioPlayerList.ContainsKey(clip.BotId)) continue;
            count++;
            if (UnityEngine.Random.Range(0, count) == 0)
                selected = clip;
        }

        if (selected == null)
        {
            if (Instance.Config.Debug)
                Log.Warn($"{listName}: no bot IDs match any spawned bot.");
            return EmptyClip;
        }

        return selected;
    }

    public static AudioFile PlayRandomAudioFile(List<AudioFile> audioClips, string listName = "")
    {
        AudioFile clip = GetRandomAudioClip(audioClips, listName);
        clip.Play();
        return clip;
    }

    public static AudioFile PlayRandomAudioFileFromPlayer(List<AudioFile> audioClips, Player player, string listName = "")
    {
        AudioFile clip = GetRandomAudioClip(audioClips, listName);
        clip.PlayFromFilePlayer(new List<int> { player.Id });
        return clip;
    }

    public static string PathCheck(string path)
    {
        if (File.Exists(path)) return path;

        string relative = Path.Combine(Instance.AudioPath, path);
        if (File.Exists(relative)) return relative;

        return path;
    }
}
