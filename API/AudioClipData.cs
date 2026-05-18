namespace EviAudio.API;

public sealed class AudioClipData
{
    public AudioClipData(string name, int sampleRate, int channels, float[] samples, AudioTrackMetadata metadata = null)
    {
        Name = name;
        SampleRate = sampleRate;
        Channels = channels;
        Samples = samples;
        Metadata = metadata ?? new AudioTrackMetadata();
    }

    public string Name { get; }
    public int SampleRate { get; }
    public int Channels { get; }
    public float[] Samples { get; }
    public AudioTrackMetadata Metadata { get; }
}
