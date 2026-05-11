using System.Text;

namespace Beutl.FFmpegWorker;

internal static class WorkerLog
{
    private static readonly object s_writeLock = new();

    public static void Information(string message, Exception? exception = null)
        => Write("Information", message, exception);

    public static void Warning(string message, Exception? exception = null)
        => Write("Warning", message, exception);

    public static void Error(string message, Exception? exception = null)
        => Write("Error", message, exception);

    public static void Write(string level, string message, Exception? exception)
    {
        // ロガー基盤の例外でホストプロセスを巻き込まないよう例外は飲み込む
        try
        {
            // ダッシュボードで1イベントとして表示できるよう、複数行は \n エスケープして1行で送る。
            // ホスト側 (FFmpegWorkerProcess) でデコードする。
            string payload = exception != null
                ? $"{message}\n{exception}"
                : message;

            string encoded = Encode(payload);

            lock (s_writeLock)
            {
                Console.Out.WriteLine($"[ffmpeg:{level}] {encoded}");
            }
        }
        catch
        {
        }
    }

    private static string Encode(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }
}
