using Beutl.FFmpegIpc.Protocol;
using Beutl.FFmpegIpc.Protocol.Messages;
using FFmpeg.AutoGen.Abstractions;
using FFmpegSharp;

namespace Beutl.FFmpegWorker.Handlers;

internal sealed class CodecQueryHandler
{
    public IpcMessage HandleQueryCodecs(IpcMessage msg)
    {
        var request = msg.GetPayload<QueryCodecsRequest>()!;
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
        var request = msg.GetPayload<QueryPixelFormatsRequest>()!;

        try
        {
            MediaCodec codec = FindVideoEncoder(request.CodecName, request.OutputFile);
            var fmts = codec.GetPixelFmts()
                .Where(f => ffmpeg.sws_isSupportedOutput(f) != 0)
                .Select(f => new PixelFormatInfo
                {
                    Value = (int)f,
                    Name = ffmpeg.av_get_pix_fmt_name(f),
                })
                .ToArray();

            return IpcMessage.Create(msg.Id, MessageType.QueryPixelFormatsResult,
                new QueryPixelFormatsResponse { Formats = fmts });
        }
        catch
        {
            // フォールバック: 全対応フォーマット
            var allFmts = Enum.GetValues<AVPixelFormat>()
                .Where(f => f != AVPixelFormat.AV_PIX_FMT_NONE && (int)f >= 0 && ffmpeg.sws_isSupportedOutput(f) != 0)
                .Select(f => new PixelFormatInfo
                {
                    Value = (int)f,
                    Name = ffmpeg.av_get_pix_fmt_name(f),
                })
                .ToArray();

            return IpcMessage.Create(msg.Id, MessageType.QueryPixelFormatsResult,
                new QueryPixelFormatsResponse { Formats = allFmts });
        }
    }

    public IpcMessage HandleQuerySampleRates(IpcMessage msg)
    {
        var request = msg.GetPayload<QuerySampleRatesRequest>()!;

        try
        {
            MediaCodec codec = FindAudioEncoder(request.CodecName, request.OutputFile);
            var rates = codec.GetSupportedSamplerates().ToArray();
            return IpcMessage.Create(msg.Id, MessageType.QuerySampleRatesResult,
                new QuerySampleRatesResponse { SampleRates = rates });
        }
        catch
        {
            return IpcMessage.Create(msg.Id, MessageType.QuerySampleRatesResult,
                new QuerySampleRatesResponse { SampleRates = [] });
        }
    }

    public IpcMessage HandleQueryAudioFormats(IpcMessage msg)
    {
        var request = msg.GetPayload<QueryAudioFormatsRequest>()!;

        try
        {
            MediaCodec codec = FindAudioEncoder(request.CodecName, request.OutputFile);
            var fmts = codec.GetSampelFmts().Select(f => (int)f).ToArray();
            return IpcMessage.Create(msg.Id, MessageType.QueryAudioFormatsResult,
                new QueryAudioFormatsResponse { Formats = fmts });
        }
        catch
        {
            return IpcMessage.Create(msg.Id, MessageType.QueryAudioFormatsResult,
                new QueryAudioFormatsResponse { Formats = [] });
        }
    }

    public IpcMessage HandleQueryDefaultCodec(IpcMessage msg)
    {
        var request = msg.GetPayload<QueryDefaultCodecRequest>()!;

        try
        {
            var outFormat = OutputFormat.GuessFormat(null, request.OutputFile, null);
            string? videoCodec = outFormat.VideoCodec != AVCodecID.AV_CODEC_ID_NONE
                ? MediaCodec.FindEncoder(outFormat.VideoCodec).Name
                : null;
            string? audioCodec = outFormat.AudioCodec != AVCodecID.AV_CODEC_ID_NONE
                ? MediaCodec.FindEncoder(outFormat.AudioCodec).Name
                : null;

            return IpcMessage.Create(msg.Id, MessageType.QueryDefaultCodecResult,
                new QueryDefaultCodecResponse { VideoCodecName = videoCodec, AudioCodecName = audioCodec });
        }
        catch
        {
            return IpcMessage.Create(msg.Id, MessageType.QueryDefaultCodecResult,
                new QueryDefaultCodecResponse());
        }
    }

    private static MediaCodec FindVideoEncoder(string? codecName, string? outputFile)
    {
        if (!string.IsNullOrEmpty(codecName) && codecName != "Default")
            return MediaCodec.FindEncoder(codecName);

        if (!string.IsNullOrEmpty(outputFile))
        {
            var outFormat = OutputFormat.GuessFormat(null, outputFile, null);
            return MediaCodec.FindEncoder(outFormat.VideoCodec);
        }

        throw new InvalidOperationException("No codec name or output file specified");
    }

    private static MediaCodec FindAudioEncoder(string? codecName, string? outputFile)
    {
        if (!string.IsNullOrEmpty(codecName) && codecName != "Default")
            return MediaCodec.FindEncoder(codecName);

        if (!string.IsNullOrEmpty(outputFile))
        {
            var outFormat = OutputFormat.GuessFormat(null, outputFile, null);
            return MediaCodec.FindEncoder(outFormat.AudioCodec);
        }

        throw new InvalidOperationException("No codec name or output file specified");
    }
}
