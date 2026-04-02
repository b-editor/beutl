#if FFMPEG_OUT_OF_PROCESS
using Beutl.FFmpegIpc.Protocol;
using Beutl.FFmpegIpc.Protocol.Messages;
#endif
using Beutl.Media.Decoding;

namespace Beutl.Extensions.FFmpeg.Decoding;

public sealed class FFmpegDecoderInfo(FFmpegDecodingSettings settings) : IDecoderInfo
{
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
#if FFMPEG_OUT_OF_PROCESS
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
#else
            return new FFmpegReader(file, options, settings);
#endif
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
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
