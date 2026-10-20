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

/// <summary>Classifies FFmpeg failures by their AVERROR code.</summary>
public static class FFmpegErrorMessageMapper
{
    // Known FFmpeg AVERROR values.
    public const int InvalidDataCode = -1094995529;
    public const int DecoderNotFoundCode = -1128613112;
    public const int DemuxerNotFoundCode = -1296385272;
    public const int ProtocolNotFoundCode = -1330794744;
    public const int StreamNotFoundCode = -1381258232;

    /// <summary>Returns a known failure kind, or null.</summary>
    /// <remarks>
    /// The worker sends the code with every FFmpeg failure, and FFmpeg4Sharp creates an exception without one
    /// only for its own filter graph messages, so the message text is not read.
    /// </remarks>
    public static FFmpegErrorKind? TryClassify(int? errorCode)
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

        return null;
    }
}
