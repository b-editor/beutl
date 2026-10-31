using Beutl.FFmpegIpc.Protocol;
using Beutl.FFmpegIpc.Protocol.Messages;
using Beutl.FFmpegIpc.SharedMemory;
using FFmpeg.AutoGen.Abstractions;

namespace Beutl.FFmpegWorker.Handlers;

internal sealed unsafe class PreviewEncodingHandler : IDisposable
{
    private SharedMemoryBuffer? _buffer;
    private PreviewEncoderStart? _settings;
    private AVCodecContext* _codec;
    private AVFrame* _frame;
    private AVPacket* _packet;
    private SwsContext* _scale;
    private long _pts;

    internal IpcMessage Start(IpcMessage message)
    {
        Dispose();
        var settings = message.RequirePayload<PreviewEncoderStart>(MessageType.StartPreviewEncoder);
        int expected = PreviewEncodingFormat.BufferBytes(settings.Width, settings.Height, settings.Hdr, settings.Audio);
        if (settings.Capacity != expected || (!settings.Audio &&
            (settings.Width < 2 || settings.Height < 2
             || settings.Width > PreviewEncodingFormat.MaxDimension || settings.Height > PreviewEncodingFormat.MaxDimension)))
            throw new ArgumentException("Invalid preview encoder dimensions.");
        _settings = settings;
        try
        {
            _buffer = SharedMemoryBuffer.Open(settings.SharedMemoryName, expected);
            string name = settings.Audio ? "libopus" : settings.Hdr ? "libvpx-vp9" : "libvpx";
            AVCodec* encoder = ffmpeg.avcodec_find_encoder_by_name(name);
            if (encoder == null) throw new NotSupportedException($"The installed FFmpeg does not provide {name}.");
            _codec = ffmpeg.avcodec_alloc_context3(encoder);
            _frame = ffmpeg.av_frame_alloc();
            _packet = ffmpeg.av_packet_alloc();
            if (_codec == null || _frame == null || _packet == null) throw new OutOfMemoryException();
            _codec->thread_count = 2;
            if (settings.Audio)
            {
                _codec->sample_rate = PreviewEncodingFormat.SampleRate;
                _codec->sample_fmt = AVSampleFormat.AV_SAMPLE_FMT_FLT;
                _codec->time_base = new AVRational { num = 1, den = PreviewEncodingFormat.SampleRate };
                _codec->bit_rate = 96000;
                ffmpeg.av_channel_layout_default(&_codec->ch_layout, PreviewEncodingFormat.AudioChannels);
                Check(ffmpeg.av_opt_set(_codec->priv_data, "application", "lowdelay", 0));
            }
            else
            {
                _codec->width = settings.Width;
                _codec->height = settings.Height;
                _codec->pix_fmt = settings.Hdr ? AVPixelFormat.AV_PIX_FMT_YUV420P10LE : AVPixelFormat.AV_PIX_FMT_YUV420P;
                _codec->profile = settings.Hdr ? 2 : 0;
                _codec->time_base = new AVRational { num = 1, den = PreviewEncodingFormat.FrameRate };
                _codec->framerate = new AVRational { num = PreviewEncodingFormat.FrameRate, den = 1 };
                _codec->gop_size = 30;
                _codec->max_b_frames = 0;
                _codec->bit_rate = settings.Hdr ? 4_000_000 : 2_000_000;
                _codec->color_primaries = settings.Hdr ? AVColorPrimaries.AVCOL_PRI_BT2020 : AVColorPrimaries.AVCOL_PRI_BT709;
                _codec->color_trc = settings.Hdr ? AVColorTransferCharacteristic.AVCOL_TRC_SMPTE2084 : AVColorTransferCharacteristic.AVCOL_TRC_IEC61966_2_1;
                _codec->colorspace = settings.Hdr ? AVColorSpace.AVCOL_SPC_BT2020_NCL : AVColorSpace.AVCOL_SPC_BT709;
                _codec->color_range = AVColorRange.AVCOL_RANGE_MPEG;
                Check(ffmpeg.av_opt_set(_codec->priv_data, "deadline", "realtime", 0));
                Check(ffmpeg.av_opt_set(_codec->priv_data, "cpu-used", settings.Hdr ? "8" : "10", 0));
                Check(ffmpeg.av_opt_set(_codec->priv_data, "lag-in-frames", "0", 0));
                if (settings.Hdr) Check(ffmpeg.av_opt_set(_codec->priv_data, "row-mt", "1", 0));
            }
            Check(ffmpeg.avcodec_open2(_codec, encoder, null));
            if (settings.Audio)
            {
                _frame->format = (int)_codec->sample_fmt;
                _frame->sample_rate = PreviewEncodingFormat.SampleRate;
                _frame->nb_samples = PreviewEncodingFormat.AudioPacketSamples;
                Check(ffmpeg.av_channel_layout_copy(&_frame->ch_layout, &_codec->ch_layout));
            }
            else
            {
                _frame->format = (int)_codec->pix_fmt;
                _frame->width = settings.Width;
                _frame->height = settings.Height;
                _frame->color_primaries = _codec->color_primaries;
                _frame->color_trc = _codec->color_trc;
                _frame->colorspace = _codec->colorspace;
                _frame->color_range = _codec->color_range;
                _scale = ffmpeg.sws_getContext(settings.Width, settings.Height,
                    settings.Hdr ? AVPixelFormat.AV_PIX_FMT_RGBA64LE : AVPixelFormat.AV_PIX_FMT_BGRA,
                    settings.Width, settings.Height, _codec->pix_fmt, (int)SwsFlags.SWS_BILINEAR, null, null, null);
                if (_scale == null) throw new InvalidOperationException("Could not create preview color conversion.");
                int4 matrix = *(int4*)ffmpeg.sws_getCoefficients(settings.Hdr ? ffmpeg.SWS_CS_BT2020 : ffmpeg.SWS_CS_ITU709);
                Check(ffmpeg.sws_setColorspaceDetails(_scale, matrix, 1, matrix, 0, 0, 1 << 16, 1 << 16));
            }
            Check(ffmpeg.av_frame_get_buffer(_frame, 0));
            return IpcMessage.CreateSimple(message.Id, MessageType.StartPreviewEncoderResult);
        }
        catch { Dispose(); throw; }
    }

    internal IpcMessage Encode(IpcMessage message)
    {
        var settings = _settings ?? throw new InvalidOperationException("Preview encoder has not started.");
        var request = message.RequirePayload<PreviewEncoderFrame>(MessageType.EncodePreviewFrame);
        Check(ffmpeg.av_frame_make_writable(_frame));
        byte* source = _buffer!.AcquirePointer();
        try
        {
            if (settings.Audio)
                new ReadOnlySpan<byte>(source, settings.Capacity).CopyTo(new Span<byte>(_frame->data[0], settings.Capacity));
            else
            {
                byte_ptr4 planes = default;
                int4 strides = default;
                planes[0] = source;
                strides[0] = settings.Width * PreviewEncodingFormat.BytesPerPixel(settings.Hdr);
                Check(ffmpeg.sws_scale(_scale, planes, strides, 0, settings.Height, _frame->data, _frame->linesize));
                _frame->pict_type = request.KeyFrame ? AVPictureType.AV_PICTURE_TYPE_I : AVPictureType.AV_PICTURE_TYPE_NONE;
            }
        }
        finally { _buffer.ReleasePointer(); }
        _frame->pts = _pts;
        _pts += settings.Audio ? PreviewEncodingFormat.AudioPacketSamples : 1;
        Check(ffmpeg.avcodec_send_frame(_codec, _frame));
        int result = ffmpeg.avcodec_receive_packet(_codec, _packet);
        if (result == ffmpeg.AVERROR(ffmpeg.EAGAIN))
            return IpcMessage.Create(message.Id, MessageType.EncodePreviewFrameResult, new PreviewEncodedPacket([]));
        Check(result);
        try
        {
            return IpcMessage.Create(message.Id, MessageType.EncodePreviewFrameResult,
                new PreviewEncodedPacket(new ReadOnlySpan<byte>(_packet->data, _packet->size).ToArray()));
        }
        finally { ffmpeg.av_packet_unref(_packet); }
    }

    private static void Check(int result)
    {
        if (result < 0) throw new InvalidOperationException($"Preview encoding failed (FFmpeg {result}).");
    }

    public void Dispose()
    {
        if (_scale != null) { ffmpeg.sws_freeContext(_scale); _scale = null; }
        if (_frame != null) { var frame = _frame; ffmpeg.av_frame_free(&frame); _frame = null; }
        if (_packet != null) { var packet = _packet; ffmpeg.av_packet_free(&packet); _packet = null; }
        if (_codec != null) { var codec = _codec; ffmpeg.avcodec_free_context(&codec); _codec = null; }
        _buffer?.Dispose(); _buffer = null; _settings = null; _pts = 0;
    }
}
