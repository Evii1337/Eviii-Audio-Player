using System;

namespace EviAudio.API;

public sealed class AudioTrackMetadata
{
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string Album { get; set; } = string.Empty;
    public TimeSpan Duration { get; set; } = TimeSpan.Zero;

    public string DisplayName(string fallback)
    {
        if (!string.IsNullOrWhiteSpace(Artist) && !string.IsNullOrWhiteSpace(Title))
            return $"{Artist} - {Title}";

        if (!string.IsNullOrWhiteSpace(Title))
            return Title;

        return fallback;
    }
}
