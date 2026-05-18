using System;

namespace EviAudio.API;

public static class PitchShifter
{
    public static float[] Shift(float[] samples, float semitones)
    {
        if (Math.Abs(semitones) < 0.01f)
            return samples;

        double ratio = Math.Pow(2.0, semitones / 12.0);
        int outputLen = (int)(samples.Length / ratio);
        if (outputLen <= 0)
            return Array.Empty<float>();

        var output = new float[outputLen];
        for (int i = 0; i < outputLen; i++)
        {
            double srcIdx = i * ratio;
            int lo = (int)srcIdx;
            int hi = lo + 1 < samples.Length ? lo + 1 : samples.Length - 1;
            double t = srcIdx - lo;
            output[i] = (float)(samples[lo] * (1.0 - t) + samples[hi] * t);
        }
        return output;
    }
}
