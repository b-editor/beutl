using System.Globalization;

namespace Beutl.FFmpegIpc;

/// <summary>
/// Worker の生エラーテキスト / FFmpeg の AVERROR コードをユーザー向けメッセージに翻訳する。
/// テレメトリでは <c>FFmpeg error [-1094995529] Invalid data found when processing input</c> のような
/// 生の AVERROR コードがそのまま通知に露出してユーザーを混乱させていたため、既知コードを
/// 人が読める説明に置き換える。UI 層だけでなく FFmpegIpc に置くのは、エンコードプロキシ
/// (<see cref="Beutl.FFmpegIpc"/>) や別ホスト (AgentToolkit 等) からも同じ翻訳を使えるようにするため。
/// </summary>
public static class FFmpegErrorMessageMapper
{
    // AVERROR codes (FFmpeg libavutil/error.h)。符号付きの素の値 (例: INVALIDDATA = FFERRTAG('I','N','D','A'))。
    public const int InvalidDataCode = -1094995529;
    public const int DecoderNotFoundCode = -1128613112;
    public const int DemuxerNotFoundCode = -1296385272;
    public const int ProtocolNotFoundCode = -1330794744;
    public const int StreamNotFoundCode = -1381258232;

    private const string InvalidDataText = "Invalid data found when processing input";

    /// <summary>
    /// 既知コード / 既知テキストをフォーマット済みの説明に翻訳する。未知のものは null を返す
    /// (呼び出し側が元の message をそのまま使う)。
    /// </summary>
    /// <param name="errorCode">AVERROR コード。<paramref name="message"/> が生テキストのみの場合は null。</param>
    /// <param name="message">
    /// Worker の生エラーテキスト (<c>FFmpeg error [{code}] {text}</c>)。FFmpegErrorCode が載らない
    /// 旧経路 (EncodeComplete など) でも翻訳できるようテキストも見る。
    /// </param>
    /// <param name="format">
    /// 説明文のフォーマット。<c>{0}</c> に人が読める説明が入る。null なら説明文だけを返す。
    /// </param>
    public static string? Translate(int? errorCode, string? message, string? format = null)
    {
        string? description = Describe(errorCode, message);
        if (description == null)
            return null;

        return format != null
            ? string.Format(CultureInfo.CurrentCulture, format, description)
            : description;
    }

    private static string? Describe(int? errorCode, string? message)
    {
        switch (errorCode)
        {
            case InvalidDataCode:
                return "The input file appears corrupt, incomplete, or still being written. " +
                       "Re-export or restore the file and try again.";
            case DecoderNotFoundCode:
                return "No decoder was found for this codec in the bundled FFmpeg build.";
            case DemuxerNotFoundCode:
                return "The container format is not supported by the bundled FFmpeg build.";
            case ProtocolNotFoundCode:
                return "The stream protocol is not supported by the bundled FFmpeg build.";
            case StreamNotFoundCode:
                return "No suitable stream was found in the input.";
        }

        // コードが取れない旧経路向け: AVERROR_INVALIDDATA の既知テキストを検出する。
        if (message != null
            && message.Contains(InvalidDataText, StringComparison.Ordinal))
        {
            return "The input file appears corrupt, incomplete, or still being written. " +
                   "Re-export or restore the file and try again.";
        }

        return null;
    }
}
