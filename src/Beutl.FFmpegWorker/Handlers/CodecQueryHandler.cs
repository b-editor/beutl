using Beutl.FFmpegIpc.Protocol;
using Beutl.FFmpegIpc.Protocol.Messages;
using FFmpeg.AutoGen.Abstractions;
using FFmpegSharp;

namespace Beutl.FFmpegWorker.Handlers;

internal sealed class CodecQueryHandler
{
    public IpcMessage HandleQueryCodecs(IpcMessage msg)
    {
        var request = msg.RequirePayload<QueryCodecsRequest>(MessageType.QueryCodecs);
        var mediaType = request.MediaType == "audio"
            ? AVMediaType.AVMEDIA_TYPE_AUDIO
            : AVMediaType.AVMEDIA_TYPE_VIDEO;

        var codecs = MediaCodec.GetCodecs()
            .Where(c => c.IsEncoder && c.Type == mediaType)
            .Select(c => new CodecInfo { Name = c.Name, LongName = c.LongName })
            .ToArray();

        return IpcMessage.Create(msg.Id, MessageType.QueryCodecsResult,
            new QueryCodecsResponse { Codecs = codecs });
    }

    public IpcMessage HandleQueryPixelFormats(IpcMessage msg)
    {
        var request = msg.RequirePayload<QueryPixelFormatsRequest>(MessageType.QueryPixelFormats);

        try
        {
            MediaCodec codec = FindVideoEncoder(request.CodecName, request.OutputFile);
            var fmts = codec.GetPixelFmts()
                .Where(f => ffmpeg.sws_isSupportedOutput(f) != 0)
                .Select(ToPixelFormatInfo)
                .ToArray();

            return IpcMessage.Create(msg.Id, MessageType.QueryPixelFormatsResult,
                new QueryPixelFormatsResponse { Formats = fmts });
        }
        catch (Exception ex)
        {
            WorkerLog.Warning($"QueryPixelFormats: codec-specific query failed, falling back to all formats: {ex.Message}", ex);
            // フォールバック: 全対応フォーマット
            var allFmts = Enum.GetValues<AVPixelFormat>()
                .Where(f => f != AVPixelFormat.AV_PIX_FMT_NONE && (int)f >= 0 && ffmpeg.sws_isSupportedOutput(f) != 0)
                .Select(ToPixelFormatInfo)
                .ToArray();

            return IpcMessage.Create(msg.Id, MessageType.QueryPixelFormatsResult,
                new QueryPixelFormatsResponse { Formats = allFmts, Degraded = true });
        }
    }

    public IpcMessage HandleQuerySampleRates(IpcMessage msg)
    {
        var request = msg.RequirePayload<QuerySampleRatesRequest>(MessageType.QuerySampleRates);

        try
        {
            MediaCodec codec = FindAudioEncoder(request.CodecName, request.OutputFile);
            var rates = codec.GetSupportedSamplerates().ToArray();
            return IpcMessage.Create(msg.Id, MessageType.QuerySampleRatesResult,
                new QuerySampleRatesResponse { SampleRates = rates });
        }
        catch (Exception ex)
        {
            WorkerLog.Warning($"QuerySampleRates: codec-specific query failed: {ex.Message}", ex);
            return IpcMessage.Create(msg.Id, MessageType.QuerySampleRatesResult,
                new QuerySampleRatesResponse { SampleRates = [], Degraded = true });
        }
    }

    public IpcMessage HandleQueryAudioFormats(IpcMessage msg)
    {
        var request = msg.RequirePayload<QueryAudioFormatsRequest>(MessageType.QueryAudioFormats);

        try
        {
            MediaCodec codec = FindAudioEncoder(request.CodecName, request.OutputFile);
            var fmts = codec.GetSampelFmts().Select(f => (int)f).ToArray();
            return IpcMessage.Create(msg.Id, MessageType.QueryAudioFormatsResult,
                new QueryAudioFormatsResponse { Formats = fmts });
        }
        catch (Exception ex)
        {
            WorkerLog.Warning($"QueryAudioFormats: codec-specific query failed: {ex.Message}", ex);
            return IpcMessage.Create(msg.Id, MessageType.QueryAudioFormatsResult,
                new QueryAudioFormatsResponse { Formats = [], Degraded = true });
        }
    }

    private static PixelFormatInfo ToPixelFormatInfo(AVPixelFormat format)
    {
        return new PixelFormatInfo
        {
            Value = (int)format,
            Name = ffmpeg.av_get_pix_fmt_name(format),
        };
    }

    private static MediaCodec FindVideoEncoder(string? codecName, string? outputFile)
        => FindEncoder(codecName, outputFile, static outFormat => outFormat.VideoCodec);

    private static MediaCodec FindAudioEncoder(string? codecName, string? outputFile)
        => FindEncoder(codecName, outputFile, static outFormat => outFormat.AudioCodec);

    private static MediaCodec FindEncoder(
        string? codecName, string? outputFile, Func<OutputFormat, AVCodecID> containerCodec)
    {
        if (!string.IsNullOrEmpty(codecName) && codecName != "Default")
            return MediaCodec.FindEncoder(codecName);

        if (!string.IsNullOrEmpty(outputFile))
        {
            var outFormat = OutputFormat.GuessFormat(null, outputFile, null);
            return MediaCodec.FindEncoder(containerCodec(outFormat));
        }

        throw new InvalidOperationException("No codec name or output file specified");
    }
}
