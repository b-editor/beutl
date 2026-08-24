using System.Globalization;

namespace Beutl.FFmpegIpc;

/// <summary>
/// Stable classification of worker-reported FFmpeg failures. Kept in the IPC layer so every host
/// can switch on it; UI hosts resolve the kind into localized resources (e.g. MessageStrings)
/// while headless hosts can fall back to <see cref="FFmpegErrorMessageMapper.Translate"/>.
/// </summary>
public enum FFmpegErrorKind
{
    /// <summary>Input data is corrupt, incomplete, or still being written (moov atom missing, etc.).</summary>
    InvalidData,

    /// <summary>No decoder is available for the codec in the bundled FFmpeg build.</summary>
    DecoderNotFound,

    /// <summary>The container format is not supported by the bundled FFmpeg build.</summary>
    DemuxerNotFound,

    /// <summary>The stream protocol is not supported by the bundled FFmpeg build.</summary>
    ProtocolNotFound,

    /// <summary>No suitable stream was found in the input.</summary>
    StreamNotFound,
}

/// <summary>
/// Classifies the worker's raw error text / FFmpeg <c>AVERROR</c> code. Telemetry showed
/// <c>FFmpeg error [-1094995529] Invalid data found when processing input</c>-style messages
/// leaking verbatim into user notifications; hosts use this to present a readable explanation
/// instead of the numeric code.
/// </summary>
public static class FFmpegErrorMessageMapper
{
    // AVERROR codes (FFmpeg libavutil/error.h) as signed raw values (e.g. INVALIDDATA = FFERRTAG('I','N','D','A')).
    public const int InvalidDataCode = -1094995529;
    public const int DecoderNotFoundCode = -1128613112;
    public const int DemuxerNotFoundCode = -1296385272;
    public const int ProtocolNotFoundCode = -1330794744;
    public const int StreamNotFoundCode = -1381258232;

    private const string InvalidDataText = "Invalid data found when processing input";

    /// <summary>
    /// Classifies a known FFmpeg failure from the worker error code / message. Returns null for
    /// unknown failures (callers keep the original message).
    /// </summary>
    /// <param name="errorCode">The AVERROR code, or null when only raw text is available.</param>
    /// <param name="message">
    /// The worker's raw error text (<c>FFmpeg error [{code}] {text}</c>). The text is also matched so
    /// legacy paths that do not carry <c>FFmpegErrorCode</c> (e.g. EncodeComplete) still classify.
    /// </param>
    public static FFmpegErrorKind? TryClassify(int? errorCode, string? message)
    {
        switch (errorCode)
        {
            case InvalidDataCode:
                return FFmpegErrorKind.InvalidData;
            case DecoderNotFoundCode:
                return FFmpegErrorKind.DecoderNotFound;
            case DemuxerNotFoundCode:
                return FFmpegErrorKind.DemuxerNotFound;
            case ProtocolNotFoundCode:
                return FFmpegErrorKind.ProtocolNotFound;
            case StreamNotFoundCode:
                return FFmpegErrorKind.StreamNotFound;
        }

        // Legacy path without a code: detect AVERROR_INVALIDDATA from its known message text.
        if (message != null
            && message.Contains(InvalidDataText, StringComparison.Ordinal))
        {
            return FFmpegErrorKind.InvalidData;
        }

        return null;
    }

    /// <summary>
    /// Translates a known FFmpeg failure into an English description, for hosts without localized
    /// resources (headless tools, logs). Returns null when the failure is unknown (callers keep the
    /// original message). <paramref name="format"/>, when given, receives the description as <c>{0}</c>.
    /// </summary>
    public static string? Translate(int? errorCode, string? message, string? format = null)
    {
        if (TryClassify(errorCode, message) is not { } kind)
            return null;

        string description = kind switch
        {
            FFmpegErrorKind.InvalidData =>
                "The input file appears corrupt, incomplete, or still being written. " +
                "Re-export or restore the file and try again.",
            FFmpegErrorKind.DecoderNotFound =>
                "No decoder was found for this codec in the bundled FFmpeg build.",
            FFmpegErrorKind.DemuxerNotFound =>
                "The container format is not supported by the bundled FFmpeg build.",
            FFmpegErrorKind.ProtocolNotFound =>
                "The stream protocol is not supported by the bundled FFmpeg build.",
            FFmpegErrorKind.StreamNotFound =>
                "No suitable stream was found in the input.",
            _ => throw new InvalidOperationException($"Unknown FFmpegErrorKind: {kind}"),
        };

        return format != null
            ? string.Format(CultureInfo.CurrentCulture, format, description)
            : description;
    }
}
