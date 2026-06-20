using Beutl.Extensions.FFmpeg.Encoding;
using Beutl.FFmpegIpc.Protocol.Messages;

namespace Beutl.Extensions.FFmpeg.PropertyEditors;

/// <summary>
/// Per-option-type caches shared across every codec property editor instance, so concurrent editors
/// using the same codec reuse one IPC query to the FFmpeg worker. Sharing is safe because
/// <see cref="FFmpegOptionsCache{T}"/> is thread-safe and each editor's refresh tracker stays
/// instance-local. Call <see cref="ClearAll"/> if the worker binary may have changed.
/// </summary>
internal static class FFmpegOptionsCaches
{
    public static FFmpegOptionsCache<FFmpegAudioEncoderSettings.AudioFormat> AudioFormats { get; } = new();

    public static FFmpegOptionsCache<PixelFormatInfo> PixelFormats { get; } = new();

    public static FFmpegOptionsCache<int> SampleRates { get; } = new();

    /// <summary>Clears all caches. Used by tests and, eventually, a worker-restart hook.</summary>
    public static void ClearAll()
    {
        AudioFormats.Clear();
        PixelFormats.Clear();
        SampleRates.Clear();
    }
}
