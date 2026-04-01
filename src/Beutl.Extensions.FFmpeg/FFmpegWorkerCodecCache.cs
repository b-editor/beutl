using Beutl.FFmpegIpc.Protocol;
using Beutl.FFmpegIpc.Protocol.Messages;
using Beutl.Extensions.FFmpeg.Encoding;

namespace Beutl.Extensions.FFmpeg;

/// <summary>
/// Workerプロセスからコーデック情報をキャッシュする。
/// 初回アクセス時にWorkerへIPC照会を行い、結果をキャッシュする。
/// </summary>
internal static class FFmpegWorkerCodecCache
{
    private static readonly object s_lock = new();
    private static volatile IReadOnlyList<object>? _videoCodecs;
    private static volatile IReadOnlyList<object>? _audioCodecs;

    public static IReadOnlyList<object> GetVideoCodecs()
    {
        var cached = _videoCodecs;
        if (cached != null) return cached;

        lock (s_lock)
        {
            cached = _videoCodecs;
            if (cached != null) return cached;
            return RefreshVideoCodecs();
        }
    }

    public static IReadOnlyList<object> GetAudioCodecs()
    {
        var cached = _audioCodecs;
        if (cached != null) return cached;

        lock (s_lock)
        {
            cached = _audioCodecs;
            if (cached != null) return cached;
            return RefreshAudioCodecs();
        }
    }

    private static IReadOnlyList<object> RefreshVideoCodecs()
    {
        try
        {
            var connection = FFmpegWorkerProcess.DecodingInstance.EnsureStartedAsync().GetAwaiter().GetResult();
            var response = connection.RequestAsync<QueryCodecsRequest, QueryCodecsResponse>(
                MessageType.QueryCodecs, MessageType.QueryCodecsResult,
                new QueryCodecsRequest { MediaType = "video" }).AsTask().GetAwaiter().GetResult();
            var result = response.Codecs
                .Select(c => (object)new CodecRecord(c.Name, c.LongName))
                .Prepend(CodecRecord.Default)
                .ToArray();
            _videoCodecs = result;
            return result;
        }
        catch
        {
            return [CodecRecord.Default];
        }
    }

    private static IReadOnlyList<object> RefreshAudioCodecs()
    {
        try
        {
            var connection = FFmpegWorkerProcess.DecodingInstance.EnsureStartedAsync().GetAwaiter().GetResult();
            var response = connection.RequestAsync<QueryCodecsRequest, QueryCodecsResponse>(
                MessageType.QueryCodecs, MessageType.QueryCodecsResult,
                new QueryCodecsRequest { MediaType = "audio" }).AsTask().GetAwaiter().GetResult();
            var result = response.Codecs
                .Select(c => (object)new CodecRecord(c.Name, c.LongName))
                .Prepend(CodecRecord.Default)
                .ToArray();
            _audioCodecs = result;
            return result;
        }
        catch
        {
            return [CodecRecord.Default];
        }
    }

    public static void Invalidate()
    {
        _videoCodecs = null;
        _audioCodecs = null;
    }
}
