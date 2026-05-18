using EviAudio.API.Container;
using Exiled.API.Features;
using Exiled.Events.EventArgs.Player;
using static EviAudio.Plugin;

namespace EviAudio.Other.DLC;

internal sealed class LobbyEvents : System.IDisposable
{
    private AudioFile _currentAudioFile;
    private bool _firstPlayerJoined;
    private AudioPlayerBot _trackedBot;

    public LobbyEvents()
    {
        Exiled.Events.Handlers.Server.RoundStarted += OnRoundStarted;
        Exiled.Events.Handlers.Player.Verified += OnVerified;
    }

    private void OnVerified(VerifiedEventArgs ev)
    {
        if (_firstPlayerJoined || ev.Player.IsNPC || Round.IsStarted) return;
        _firstPlayerJoined = true;
        StartLobbySound();
    }

    private void OnRoundStarted()
    {
        _currentAudioFile?.Stop();
        UnsubscribeTrack();
        Cleanup();
    }

    public void Dispose()
    {
        _currentAudioFile?.Stop();
        Cleanup();
    }

    private void OnTrackFinished(string _)
    {
        if (!Round.IsLobby) { Cleanup(); return; }
        StartLobbySound();
    }

    private void StartLobbySound()
    {
        UnsubscribeTrack();
        _currentAudioFile = Extensions.PlayRandomAudioFile(Instance.Config.LobbyPlaylist, "LobbyPlaylist");

        if (_currentAudioFile == Extensions.EmptyClip) return;

        _trackedBot = AudioPlayerList.TryGetValue(_currentAudioFile.BotId, out var bot) ? bot : null;
        if (_trackedBot != null)
            _trackedBot.OnTrackFinished += OnTrackFinished;
    }

    private void UnsubscribeTrack()
    {
        if (_trackedBot != null)
            _trackedBot.OnTrackFinished -= OnTrackFinished;
        _trackedBot = null;
    }

    private void Cleanup()
    {
        UnsubscribeTrack();
        _currentAudioFile = null;
        _firstPlayerJoined = false;
        Exiled.Events.Handlers.Server.RoundStarted -= OnRoundStarted;
        Exiled.Events.Handlers.Player.Verified -= OnVerified;
        Plugin.LobbyEvents = null;
    }
}
