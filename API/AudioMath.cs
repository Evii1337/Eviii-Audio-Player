using System;

namespace EviAudio.API;

public static class AudioMath
{
    public static float Clamp(float value, float min, float max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }

    public static int Clamp(int value, int min, int max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }

    public static float Clamp01(float value) => Clamp(value, 0f, 1f);

    public static float SoftLimit(float value)
    {
        float sign = Math.Sign(value);
        float abs = Math.Abs(value);
        return abs > 0.9f ? sign * (0.9f + (abs - 0.9f) / (1f + (abs - 0.9f) / 0.1f)) : value;
    }
}
