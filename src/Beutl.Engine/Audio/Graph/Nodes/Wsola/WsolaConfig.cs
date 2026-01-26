namespace Beutl.Audio.Graph.Nodes;

/// <summary>
/// Configuration for WSOLA (Waveform Similarity Overlap-Add) algorithm.
/// </summary>
internal readonly struct WsolaConfig
{
    /// <summary>
    /// Frame size in milliseconds (default: 25ms).
    /// </summary>
    public float FrameSizeMs { get; init; }

    /// <summary>
    /// Overlap ratio between frames (default: 0.5).
    /// </summary>
    public float OverlapRatio { get; init; }

    /// <summary>
    /// Search range in milliseconds for finding optimal overlap position (default: 15ms).
    /// </summary>
    public float SearchRangeMs { get; init; }

    /// <summary>
    /// Creates default WSOLA configuration.
    /// </summary>
    public static WsolaConfig Default => new()
    {
        FrameSizeMs = 25f,
        OverlapRatio = 0.5f,
        SearchRangeMs = 15f
    };

    /// <summary>
    /// Calculates frame size in samples.
    /// </summary>
    public int GetFrameSizeSamples(int sampleRate) => (int)(FrameSizeMs / 1000f * sampleRate);

    /// <summary>
    /// Calculates hop size (synthesis step) in samples.
    /// </summary>
    public int GetHopSizeSamples(int sampleRate) => (int)(GetFrameSizeSamples(sampleRate) * (1f - OverlapRatio));

    /// <summary>
    /// Calculates search range in samples.
    /// </summary>
    public int GetSearchRangeSamples(int sampleRate) => (int)(SearchRangeMs / 1000f * sampleRate);
}
