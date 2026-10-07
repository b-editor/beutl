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
        return bytes switch
        {
            >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:0.#} GB",
            >= 1_048_576 => $"{bytes / 1_048_576.0:0.#} MB",
            >= 1024 => $"{bytes / 1024.0:0.#} KB",
            _ => $"{bytes} B"
        };
    }
}
