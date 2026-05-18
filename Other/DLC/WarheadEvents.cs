using Exiled.API.Features;
using Exiled.Events.EventArgs.Warhead;
using static EviAudio.Plugin;

namespace EviAudio.Other.DLC;

internal sealed class WarheadEvents
{
    private static AudioFile _currentAudioFile;

    public WarheadEvents()
    {
        Exiled.Events.Handlers.Warhead.Starting += OnWarheadStarting;
        Exiled.Events.Handlers.Warhead.Stopping += OnWarheadStopping;
        Exiled.Events.Handlers.Warhead.Detonated += OnWarheadDetonated;
    }

    public void Dispose()
    {
        Exiled.Events.Handlers.Warhead.Starting -= OnWarheadStarting;
        Exiled.Events.Handlers.Warhead.Stopping -= OnWarheadStopping;
        Exiled.Events.Handlers.Warhead.Detonated -= OnWarheadDetonated;
        _currentAudioFile = null;
    }

    private static void OnWarheadStarting(StartingEventArgs ev)
    {
        if (!Warhead.CanBeStarted || !Instance.Config.WarheadEnabled) return;
        _currentAudioFile = Extensions.PlayRandomAudioFile(Instance.Config.WarheadStartingClip, "WarheadStartingClip");
    }

    private static void OnWarheadDetonated() => _currentAudioFile?.Stop();

    private static void OnWarheadStopping(StoppingEventArgs ev)
    {
        if (Instance.Config.WarheadStopping)
            _currentAudioFile?.Stop();

        if (Instance.Config.WarheadStoppingEnabled)
            _currentAudioFile = Extensions.PlayRandomAudioFile(Instance.Config.WarheadStoppingClip, "WarheadStoppingClip");
    }
}
