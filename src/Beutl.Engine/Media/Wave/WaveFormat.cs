namespace Beutl.Media.Wave;

public record WaveFormat
{
    public required WaveFormatTag FormatTag { get; init; }

    public required short Channels { get; init; }

    public required int SamplesPerSec { get; init; }

    public required int AvgBytesPerSec { get; init; }

    public required short BlockAlign { get; init; }

    public required short BitsPerSample { get; init; }
}
