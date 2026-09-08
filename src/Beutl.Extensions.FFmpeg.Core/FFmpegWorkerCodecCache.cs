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
    private static volatile IReadOnlyList<object>? _videoCodecs;
    private static volatile IReadOnlyList<object>? _audioCodecs;
    private static bool s_missingQueryInFlight;

    public static IReadOnlyList<object> GetVideoCodecs()
    {
        var cached = _videoCodecs;
        if (cached != null) return cached;

        (IReadOnlyList<object> Codecs, FFmpegLibrariesNotFoundException? MissingException) query;
        Action? dispatchMissingNotification = null;
        bool wasKnownMissing = false;
        lock (s_lock)
        {
            cached = _videoCodecs;
            if (cached != null) return cached;
            if (s_missingQueryInFlight)
                return [CodecRecord.Default];

            query = RefreshVideoCodecs();
            if (query.MissingException is not null)
            {
                wasKnownMissing = FFmpegLibraryState.RecordMissingObservedDeferred(
                    out dispatchMissingNotification);
                s_missingQueryInFlight = true;
            }
        }

        try
        {
            if (dispatchMissingNotification is not null)
            {
                dispatchMissingNotification();
                LogMissingIfNeeded(query.MissingException!, wasKnownMissing, "video");
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

    public static IReadOnlyList<object> GetAudioCodecs()
    {
        var cached = _audioCodecs;
        if (cached != null) return cached;

        (IReadOnlyList<object> Codecs, FFmpegLibrariesNotFoundException? MissingException) query;
        Action? dispatchMissingNotification = null;
        bool wasKnownMissing = false;
        lock (s_lock)
        {
            cached = _audioCodecs;
            if (cached != null) return cached;
            if (s_missingQueryInFlight)
                return [CodecRecord.Default];

            query = RefreshAudioCodecs();
            if (query.MissingException is not null)
            {
                wasKnownMissing = FFmpegLibraryState.RecordMissingObservedDeferred(
                    out dispatchMissingNotification);
                s_missingQueryInFlight = true;
            }
        }

        try
        {
            if (dispatchMissingNotification is not null)
            {
                dispatchMissingNotification();
                LogMissingIfNeeded(query.MissingException!, wasKnownMissing, "audio");
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
        RefreshVideoCodecs()
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
            return (result, null);
        }
        catch (FFmpegLibrariesNotFoundException ex)
        {
            return ([CodecRecord.Default], ex);
        }
        catch (Exception ex)
        {
            s_logger.LogError(ex, "Failed to query video codecs from worker");
            return ([CodecRecord.Default], null);
        }
    }

    private static (IReadOnlyList<object> Codecs, FFmpegLibrariesNotFoundException? MissingException)
        RefreshAudioCodecs()
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
            return (result, null);
        }
        catch (FFmpegLibrariesNotFoundException ex)
        {
            return ([CodecRecord.Default], ex);
        }
        catch (Exception ex)
        {
            s_logger.LogError(ex, "Failed to query audio codecs from worker");
            return ([CodecRecord.Default], null);
        }
    }

    private static void LogMissingIfNeeded(
        FFmpegLibrariesNotFoundException exception,
        bool wasKnownMissing,
        string mediaType)
    {
        // Only the first discovery is an error; later attempts are expected short-circuits that
        // would otherwise spam the log every time the codec list is opened without FFmpeg.
        if (wasKnownMissing)
            s_logger.LogDebug(exception, "FFmpeg libraries missing; skipping {MediaType} codec query", mediaType);
        else
            s_logger.LogError(exception, "Failed to query {MediaType} codecs from worker", mediaType);
    }

    public static void Invalidate()
    {
        _videoCodecs = null;
        _audioCodecs = null;
    }
}
