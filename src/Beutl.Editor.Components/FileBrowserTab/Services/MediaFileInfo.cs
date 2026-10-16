namespace Beutl.Editor.Components.FileBrowserTab.Services;

public sealed record MediaFileInfo(
    int? Width,
    int? Height,
    TimeSpan? Duration,
    double? FrameRate,
    string? VideoCodec,
    string? AudioCodec,
    int? SampleRate,
    int? NumChannels,
    long FileSize)
{
    public string ToDisplayString()
    {
        var parts = new List<string>();

        if (Width.HasValue && Height.HasValue)
        {
            parts.Add($"{Width}×{Height}");
        }

        if (FrameRate.HasValue)
        {
            parts.Add($"{FrameRate.Value:0.##}fps");
        }

        if (VideoCodec != null)
        {
            parts.Add(VideoCodec);
        }
        else if (AudioCodec != null)
        {
            parts.Add(AudioCodec);
        }

        if (SampleRate.HasValue)
        {
            parts.Add($"{SampleRate.Value}Hz");
        }

        if (NumChannels.HasValue)
        {
            parts.Add(NumChannels.Value switch
            {
                1 => "Mono",
                2 => "Stereo",
                _ => $"{NumChannels.Value}ch"
            });
        }

        if (Duration.HasValue)
        {
            parts.Add(Duration.Value.TotalHours >= 1
                ? Duration.Value.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
                : Duration.Value.ToString(@"m\:ss", CultureInfo.InvariantCulture));
        }

        parts.Add(FormatFileSize(FileSize));

        return string.Join(" · ", parts);
    }

    public static string FormatFileSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.#} {units[unit]}";
    }
}
