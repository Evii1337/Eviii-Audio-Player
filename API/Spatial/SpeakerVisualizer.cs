using Exiled.API.Features;
using Exiled.API.Features.Toys;
using MEC;
using UnityEngine;

namespace EviAudio.API.Spatial;

public static class SpeakerVisualizer
{
    public static void Show(Vector3 position, float minDistance, float maxDistance, float duration = 5f)
    {
        var inner = Primitive.Create(
            PrimitiveType.Sphere,
            position,
            Vector3.zero,
            Vector3.one * minDistance * 2f,
            true,
            new Color(0f, 1f, 0.4f, 0.25f));

        var outer = Primitive.Create(
            PrimitiveType.Sphere,
            position,
            Vector3.zero,
            Vector3.one * maxDistance * 2f,
            true,
            new Color(0f, 0.6f, 1f, 0.12f));

        Timing.CallDelayed(duration, () =>
        {
            if (inner?.Base != null) inner.Destroy();
            if (outer?.Base != null) outer.Destroy();
        });
    }

    public static void ShowAll(float duration = 5f)
    {
        foreach (var kvp in SpatialAudioRegistry.All)
        {
            var player = kvp.Value;
            if (player?.Speaker == null) continue;
            Show(
                player.Speaker.transform.position,
                player.Speaker.NetworkMinDistance,
                player.Speaker.NetworkMaxDistance,
                duration);
        }
    }
}
