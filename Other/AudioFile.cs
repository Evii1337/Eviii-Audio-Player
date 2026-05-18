using EviAudio.API;
using EviAudio.API.Container;
using Exiled.API.Features;
using System;
using System.Collections.Generic;
using VoiceChat;

namespace EviAudio.Other;

public class AudioFile
{
    public AudioFile() { }

    public AudioFile(
        string path,
        bool loop = false,
        int volume = 100,
        VoiceChatChannel voiceChatChannel = VoiceChatChannel.Intercom,
        int botId = 99)
    {
        Path = path;
        Loop = loop;
        Volume = volume;
        VoiceChatChannel = voiceChatChannel;
        BotId = botId;
    }

    public string Path { get; set; }
    public bool Loop { get; set; }
    public int Volume { get; set; } = 100;
    public VoiceChatChannel VoiceChatChannel { get; set; } = VoiceChatChannel.Intercom;
    public int BotId { get; set; } = 99;

    private AudioPlayerBot Bot => AudioController.TryGetAudioPlayerContainer(BotId);

    public void Play()
    {
        try { Bot?.PlayFile(Path, Volume, Loop, VoiceChatChannel); }
        catch (Exception ex) { Log.Debug($"AudioFile.Play: {ex.Message}"); }
    }

    public void PlayFromFilePlayer(List<int> playerIds)
    {
        try { Bot?.PlayFile(Path, Volume, Loop, VoiceChatChannel, targetPlayerIds: playerIds); }
        catch (Exception ex) { Log.Debug($"AudioFile.PlayFromFilePlayer: {ex.Message}"); }
    }

    public void Stop()
    {
        try { Bot?.StopAudio(); }
        catch (Exception ex) { Log.Debug($"AudioFile.Stop: {ex.Message}"); }
    }
}
