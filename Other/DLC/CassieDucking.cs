using EviAudio.API;
using EviAudio.API.Container;
using Exiled.Events.EventArgs.Cassie;
using MEC;
using System;

namespace EviAudio.Other.DLC;

internal sealed class CassieDucking
{
    private CoroutineHandle _restoreHandle;

    public CassieDucking()
    {
        Exiled.Events.Handlers.Cassie.SendingCassieMessage += OnSendingCassie;
    }

    public void Dispose()
    {
        Exiled.Events.Handlers.Cassie.SendingCassieMessage -= OnSendingCassie;

        if (_restoreHandle.IsRunning)
            Timing.KillCoroutines(_restoreHandle);
    }

    private void OnSendingCassie(SendingCassieMessageEventArgs ev)
    {
        if (Plugin.Instance?.Config == null) return;

        float duckVol = Plugin.Instance.Config.CassieDuckVolume;
        float fadeIn = Plugin.Instance.Config.CassieDuckFadeIn;
        float fadeOut = Plugin.Instance.Config.CassieDuckFadeOut;
        var channels = Plugin.Instance.Config.CassieDuckChannels;

        foreach (AudioPlayerBot bot in AudioController.GetAllAudioPlayers())
        {
            if (!bot.IsPlaying) continue;
            if (channels.Count > 0 && !channels.Contains(bot.VoiceChatChannel.ToString())) continue;
            bot.Duck(duckVol, fadeIn);
        }

        if (_restoreHandle.IsRunning)
            Timing.KillCoroutines(_restoreHandle);

        float cassieWordCount = ev.Words?.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length ?? 3;
        float estimatedDuration = cassieWordCount * 0.6f + 2f;

        _restoreHandle = Timing.CallDelayed(estimatedDuration, () =>
        {
            foreach (AudioPlayerBot bot in AudioController.GetAllAudioPlayers())
                bot.Unduck(fadeOut);
        });
    }
}
