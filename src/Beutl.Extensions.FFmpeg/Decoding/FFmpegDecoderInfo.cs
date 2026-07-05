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
        catch (FFmpegLibrariesNotFoundException)
        {
            // A missing FFmpeg environment is not a "this decoder can't read this file" signal (which
            // null conveys, letting the registry fall through to the next decoder). Surface it so
            // callers such as proxy generation can pause the queue instead of failing per-file.
            throw;
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
