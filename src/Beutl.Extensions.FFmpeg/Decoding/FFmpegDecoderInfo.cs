using Beutl.FFmpegIpc;
using Beutl.FFmpegIpc.Protocol;
using Beutl.FFmpegIpc.Protocol.Messages;
using Beutl.Logging;
using Beutl.Media.Decoding;
using Microsoft.Extensions.Logging;

namespace Beutl.Extensions.FFmpeg.Decoding;

public sealed class FFmpegDecoderInfo(FFmpegDecodingSettings settings) : IDecoderInfo
{
    private readonly ILogger _logger = Log.CreateLogger<FFmpegDecoderInfo>();

    public string Name => "FFmpeg Decoder";

    public IEnumerable<string> AudioExtensions()
    {
        yield return ".mp3";
        yield return ".ogg";
        yield return ".wav";
        yield return ".aac";
        yield return ".wma";
        yield return ".m4a";
        yield return ".webm";
        yield return ".opus";
    }

    public MediaReader? Open(string file, MediaOptions options)
    {
        try
        {
            var worker = FFmpegWorkerProcess.DecodingInstance;
            var connection = worker.EnsureStarted();

            var request = new OpenFileRequest
            {
                FilePath = file,
                StreamsToLoad = (int)options.StreamsToLoad,
                ThreadCount = settings.ThreadCount,
                Acceleration = (int)settings.Acceleration,
                ForceSrgbGamma = settings.ForceSrgbGamma,
            };

            var response = connection.RequestAsync<OpenFileRequest, OpenFileResponse>(
                MessageType.OpenFile, MessageType.OpenFileResult, request).GetAwaiter().GetResult();

            return new FFmpegReaderProxy(connection, response.ReaderId, response);
        }
        catch (FFmpegLibrariesNotFoundException ex)
        {
            // Record the missing-libraries condition (proxy generation reads it to translate a
            // fallback-less open failure into "unavailable") but still return null, so a regular open
            // can fall through to another decoder — e.g. MediaFoundation can open an MP4 without FFmpeg.
            // Observe-only (no cooldown re-arm): a real worker-start failure arms it; re-arming on a
            // short-circuited decode would keep the re-probe window from ever elapsing.
            FFmpegInstallNotifier.MarkMissingObserved();
            _logger.LogError(ex, "Failed to open media file '{File}'", file);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open media file '{File}'", file);
            return null;
        }
    }

    public IEnumerable<string> VideoExtensions()
    {
        yield return ".avi";
        yield return ".mov";
        yield return ".wmv";
        yield return ".mp4";
        yield return ".webm";
        yield return ".mkv";
        yield return ".flv";
        yield return ".264";
        yield return ".mpeg";
        yield return ".ts";
        yield return ".mts";
        yield return ".m2ts";
    }
}
