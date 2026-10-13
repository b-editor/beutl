using Beutl.Extensions.FFmpeg.Encoding;
using Beutl.FFmpegIpc;
using Beutl.FFmpegIpc.Protocol;
using Beutl.FFmpegIpc.Protocol.Messages;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Extensions.FFmpeg;

/// <summary>
/// Workerプロセスからコーデック情報をキャッシュする。
/// 初回アクセス時にWorkerへIPC照会を行い、結果をキャッシュする。
/// </summary>
internal static class FFmpegWorkerCodecCache
{
    private static readonly ILogger s_logger = Log.CreateLogger(typeof(FFmpegWorkerCodecCache));
    private static readonly object s_lock = new();
    // Accessed through Volatile.Read/Write.
    private static IReadOnlyList<object>? _videoCodecs;
    private static IReadOnlyList<object>? _audioCodecs;
    private static bool s_missingQueryInFlight;

    public static IReadOnlyList<object> GetVideoCodecs() => GetCodecs(ref _videoCodecs, "video");

    public static IReadOnlyList<object> GetAudioCodecs() => GetCodecs(ref _audioCodecs, "audio");

    private static IReadOnlyList<object> GetCodecs(ref IReadOnlyList<object>? cache, string mediaType)
    {
        var cached = Volatile.Read(ref cache);
        if (cached != null) return cached;

        (IReadOnlyList<object> Codecs, FFmpegLibrariesNotFoundException? MissingException) query;
        Action? dispatchMissingNotification = null;
        lock (s_lock)
        {
            cached = Volatile.Read(ref cache);
            if (cached != null) return cached;
            if (s_missingQueryInFlight)
                return [CodecRecord.Default];

            query = RefreshCodecs(ref cache, mediaType);
            if (query.MissingException is not null)
            {
                FFmpegLibraryState.RecordMissingObservedDeferred(
                    out dispatchMissingNotification);
                s_missingQueryInFlight = true;
            }
        }

        try
        {
            if (dispatchMissingNotification is not null)
            {
                dispatchMissingNotification();
            }

            return query.Codecs;
        }
        finally
        {
            if (dispatchMissingNotification is not null)
            {
                lock (s_lock)
                    s_missingQueryInFlight = false;
            }
        }
    }

    private static (IReadOnlyList<object> Codecs, FFmpegLibrariesNotFoundException? MissingException)
        RefreshCodecs(ref IReadOnlyList<object>? cache, string mediaType)
    {
        try
        {
            var connection = FFmpegWorkerProcess.DecodingInstance.EnsureStartedAsync().GetAwaiter().GetResult();
            var response = connection.RequestAsync<QueryCodecsRequest, QueryCodecsResponse>(
                MessageType.QueryCodecs, MessageType.QueryCodecsResult,
                new QueryCodecsRequest { MediaType = mediaType }).AsTask().GetAwaiter().GetResult();
            var result = response.Codecs
                .Select(c => (object)new CodecRecord(c.Name, c.LongName))
                .Prepend(CodecRecord.Default)
                .ToArray();
            Volatile.Write(ref cache, result);
            return (result, null);
        }
        catch (FFmpegLibrariesNotFoundException ex)
        {
            return ([CodecRecord.Default], ex);
        }
        catch (Exception ex)
        {
            s_logger.LogError(ex, "Failed to query {MediaType} codecs from worker", mediaType);
            return ([CodecRecord.Default], null);
        }
    }

    // Test-only: resets the process-wide cache between tests.
    public static void Invalidate()
    {
        Volatile.Write(ref _videoCodecs, null);
        Volatile.Write(ref _audioCodecs, null);
    }
}
