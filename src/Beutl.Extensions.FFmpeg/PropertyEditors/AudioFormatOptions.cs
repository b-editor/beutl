using AudioFormat = Beutl.Extensions.FFmpeg.Encoding.FFmpegAudioEncoderSettings.AudioFormat;

namespace Beutl.Extensions.FFmpeg.PropertyEditors;

/// <summary>
/// The audio-format option set the editor offers, and the rule for turning a worker query result into
/// that set. Kept out of the ViewModel so the degraded-handling decision can be unit tested directly.
/// </summary>
internal static class AudioFormatOptions
{
    /// <summary>Every selectable format. Excludes Default ("Auto"), which the editor prepends itself.</summary>
    public static AudioFormat[] All()
        => Enum.GetValues<AudioFormat>()
            .Where(f => f != AudioFormat.Default)
            .ToArray();

    /// <summary>
    /// A degraded (worker-fallback) result is shown as every format rather than its empty payload:
    /// applying the empty list would mark the current format unsupported and reset the model value to
    /// Default, so the full set keeps the current selection intact.
    /// </summary>
    public static AudioFormat[] ResolveSupported(OptionsQueryResult<AudioFormat> result)
        => result.Degraded ? All() : result.Items;
}
