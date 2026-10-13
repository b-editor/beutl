namespace Beutl.FFmpegIpc;

/// <summary>Known categories for FFmpeg failures.</summary>
public enum FFmpegErrorKind
{
    InvalidData,
    DecoderNotFound,
    DemuxerNotFound,
    ProtocolNotFound,
    StreamNotFound,
}

/// <summary>Classifies FFmpeg failures by code or message.</summary>
public static class FFmpegErrorMessageMapper
{
    // Known FFmpeg AVERROR values.
    public const int InvalidDataCode = -1094995529;
    public const int DecoderNotFoundCode = -1128613112;
    public const int DemuxerNotFoundCode = -1296385272;
    public const int ProtocolNotFoundCode = -1330794744;
    public const int StreamNotFoundCode = -1381258232;

    private const string InvalidDataText = "Invalid data found when processing input";

    /// <summary>Returns a known failure kind, or null.</summary>
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

        // Support legacy messages without a code.
        if (message != null
            && message.Contains(InvalidDataText, StringComparison.Ordinal))
        {
            return FFmpegErrorKind.InvalidData;
        }

        return null;
    }
}
