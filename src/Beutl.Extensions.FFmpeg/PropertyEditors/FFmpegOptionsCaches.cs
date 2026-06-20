using Beutl.Extensions.FFmpeg.Encoding;
using Beutl.FFmpegIpc.Protocol.Messages;

namespace Beutl.Extensions.FFmpeg.PropertyEditors;

/// <summary>
/// The three per-option-type caches shared across every codec property editor instance, so that
/// concurrent editors using the same codec reuse one IPC query to the FFmpeg worker instead of each
/// issuing their own. The generic type arguments differ, so each cache is naturally independent.
/// </summary>
/// <remarks>
/// Sharing is safe because <see cref="FFmpegOptionsCache{T}"/> is thread-safe (single-flight + lock)
/// and each editor's <c>LatestRefreshTracker</c> stays instance-local. A cached option list is
/// FFmpeg-build-determined, so it remains valid across worker restarts; call <see cref="ClearAll"/>
/// defensively if the worker binary may have changed. Because these caches live for the process,
/// each <see cref="FFmpegOptionsCache{T}"/> bounds its own entries with a least-recently-used cap so
/// keys accumulated across many exports (the key embeds the output file path) cannot grow without limit.
/// </remarks>
internal static class FFmpegOptionsCaches
{
    public static FFmpegOptionsCache<FFmpegAudioEncoderSettings.AudioFormat> AudioFormats { get; } = new();

    public static FFmpegOptionsCache<PixelFormatInfo> PixelFormats { get; } = new();

    public static FFmpegOptionsCache<int> SampleRates { get; } = new();

    /// <summary>Clears all three caches. Intended for tests and, eventually, a worker-restart hook.</summary>
    public static void ClearAll()
    {
        AudioFormats.Clear();
        PixelFormats.Clear();
        SampleRates.Clear();
    }
}
