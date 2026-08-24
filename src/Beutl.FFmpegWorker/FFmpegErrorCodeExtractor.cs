using FFmpegSharp;
using Beutl.FFmpegIpc;
using Beutl.FFmpegIpc.Protocol;

namespace Beutl.FFmpegWorker;

/// <summary>
/// Worker 側例外から FFmpeg の AVERROR コードを取り出す。IPC のエラーエンベロープ
/// (<see cref="IpcMessage.ErrorCode"/> / <see cref="FFmpegWorkerException.FFmpegErrorCode"/>)
/// に載せることで、ホスト側がパースせずに既知コードをユーザー向けメッセージへ翻訳できる。
/// </summary>
internal static class FFmpegErrorCodeExtractor
{
    public static int? TryGetFFmpegErrorCode(Exception exception)
    {
        for (Exception? ex = exception; ex != null; ex = ex.InnerException)
        {
            if (ex is FFmpegException ffmpegEx)
            {
                // ThrowIfError は FFmpegException(error) (AVERROR コード保持) を投げ、
                // プレーン文字列コンストラクタ経由の FFmpegException はコードを持たない (ErrorCode == 0)。
                if (ffmpegEx.ErrorCode != 0)
                    return ffmpegEx.ErrorCode;
            }
        }

        return null;
    }
}
