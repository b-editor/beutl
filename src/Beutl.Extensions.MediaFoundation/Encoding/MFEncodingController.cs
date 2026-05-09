using System.Runtime.Versioning;
using Beutl.Extensibility;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;
using Microsoft.Extensions.Logging;
using Vortice.MediaFoundation;

#if MF_BUILD_IN
namespace Beutl.Embedding.MediaFoundation.Encoding;
using Beutl.Embedding.MediaFoundation;
using Beutl.Embedding.MediaFoundation.Decoding;
#else
namespace Beutl.Extensions.MediaFoundation.Encoding;
using Beutl.Extensions.MediaFoundation;
using Beutl.Extensions.MediaFoundation.Decoding;
#endif

#pragma warning disable CA1416 // Validate platform compatibility (SupportedOSPlatform on the Extension).

[SupportedOSPlatform("windows")]
public class MFEncodingController : EncodingController
{
    private const int AudioFrameSize = 1024;
    // 1 second in Media Foundation's 100-ns unit base.
    private const long HnsPerSecond = 10_000_000L;

    private readonly ILogger _logger = Log.CreateLogger<MFEncodingController>();

    public MFEncodingController(string outputFile) : base(outputFile) { }

    public override MFVideoEncoderSettings VideoSettings { get; } = new();

    public override MFAudioEncoderSettings AudioSettings { get; } = new();

    public override async ValueTask Encode(
        IFrameProvider frameProvider,
        ISampleProvider sampleProvider,
        CancellationToken cancellationToken)
    {
        // Media Foundation mandates a per-thread COM apartment + MFStartup. The shared
        // dispatcher already owns that lifecycle for decoding — encoders join the same
        // thread to avoid double-initializing MFStartup and to match MFReader's model.
        await MFThread.Dispatcher.InvokeAsync(() =>
            EncodeCore(frameProvider, sampleProvider, cancellationToken));
    }

    private void EncodeCore(
        IFrameProvider frameProvider,
        ISampleProvider sampleProvider,
        CancellationToken cancellationToken)
    {
        bool isHdr = VideoSettings.IsHdr;
        bool audioOnly = IsAudioOnlyContainer(OutputFile);

        using IMFAttributes sinkAttrs = MediaFactory.MFCreateAttributes(2);
        // Lets Sink Writer pick hardware encoder MFTs (QSV / NVENC / AMD VCE).
        // Without this the writer silently falls back to the software encoder.
        sinkAttrs.Set(SinkWriterAttributeKeys.ReadwriteEnableHardwareTransforms, 1);
        sinkAttrs.Set(SinkWriterAttributeKeys.DisableThrottling, 0);

        using IMFSinkWriter sinkWriter = MediaFactory.MFCreateSinkWriterFromURL(OutputFile, null, sinkAttrs);

        // Audio-only containers (.wav/.mp3/.aac/.adts/.m4a) cannot mux a video stream;
        // adding one here makes BeginWriting fail. Pure-audio outputs run through the
        // audio path only.
        int videoStreamIndex = audioOnly ? -1 : ConfigureVideoStream(sinkWriter, isHdr);
        int audioChannels = ResolveAudioChannels();
        int audioStreamIndex = ConfigureAudioStream(sinkWriter, sampleProvider.SampleRate, audioChannels);

        // Pre-compute the HDR target color space once per encode. Reusing the object
        // saves Skia from rebuilding the SKColorSpace on every frame convert.
        BitmapColorSpace? hdrTargetColorSpace = null;
        if (!audioOnly && isHdr)
        {
            hdrTargetColorSpace = MFColorSpaceHelper.BuildHdrColorSpace(
                MapTransferForHelper(VideoSettings.ColorTransfer),
                MapPrimariesForHelper(VideoSettings.ColorPrimaries));
        }

        sinkWriter.BeginWriting();

        try
        {
            long frameCount = 0;
            long sampleCount = 0;
            bool encodeVideo = videoStreamIndex >= 0;
            bool encodeAudio = audioStreamIndex >= 0;

            long frameRateNum = VideoSettings.FrameRate.Numerator;
            long frameRateDen = VideoSettings.FrameRate.Denominator;
            long sampleRate = sampleProvider.SampleRate;

            while ((encodeVideo || encodeAudio) && !cancellationToken.IsCancellationRequested)
            {
                long videoTs = (encodeVideo && frameRateNum > 0)
                    ? frameCount * frameRateDen * HnsPerSecond / frameRateNum
                    : long.MaxValue;
                long audioTs = (encodeAudio && sampleRate > 0)
                    ? sampleCount * HnsPerSecond / sampleRate
                    : long.MaxValue;

                if (encodeVideo && videoTs <= audioTs)
                {
                    WriteVideoFrame(sinkWriter, videoStreamIndex, frameProvider,
                        frameCount, frameRateNum, frameRateDen, hdrTargetColorSpace, isHdr)
                        .GetAwaiter().GetResult();
                    frameCount++;
                    if (frameCount >= frameProvider.FrameCount)
                    {
                        encodeVideo = false;
                    }
                }
                else if (encodeAudio)
                {
                    WriteAudioFrame(sinkWriter, audioStreamIndex, sampleProvider,
                        sampleCount, AudioFrameSize, sampleRate, audioChannels)
                        .GetAwaiter().GetResult();
                    sampleCount += AudioFrameSize;
                    if (sampleCount >= sampleProvider.SampleCount)
                    {
                        encodeAudio = false;
                    }
                }
                else
                {
                    break;
                }
            }
        }
        finally
        {
            // Finalize flushes encoder queues and writes the container footer. Skipping
            // this produces a partial file that most demuxers refuse to open.
            try
            {
                sinkWriter.Finalize();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "IMFSinkWriter.Finalize failed");
                throw;
            }
        }
    }

    // -------------------- Video stream setup --------------------

    private int ConfigureVideoStream(IMFSinkWriter sinkWriter, bool isHdr)
    {
        int width = VideoSettings.DestinationSize.Width;
        int height = VideoSettings.DestinationSize.Height;
        int bitrate = VideoSettings.Bitrate;
        long frNum = VideoSettings.FrameRate.Numerator;
        long frDen = VideoSettings.FrameRate.Denominator;

        // HDR implies HEVC Main10 even if the user left Codec at H264 / Default.
        // AV1 / H.264 have no defined HDR10 path in Media Foundation.
        Guid outSubType;
        bool useHevcMain10 = isHdr || VideoSettings.Codec == MFVideoEncoderSettings.VideoCodec.HEVC;
        if (useHevcMain10)
        {
            outSubType = VideoFormatGuids.Hevc;
        }
        else
        {
            outSubType = VideoFormatGuids.H264;
        }

        // Output (compressed) media type.
        using IMFMediaType outType = MediaFactory.MFCreateMediaType();
        outType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
        outType.Set(MediaTypeAttributeKeys.Subtype, outSubType);
        outType.Set(MediaTypeAttributeKeys.AvgBitrate, bitrate);
        outType.Set(MediaTypeAttributeKeys.InterlaceMode, 2u); // Progressive
        outType.Set(MediaTypeAttributeKeys.FrameSize, PackUint64((uint)width, (uint)height));
        outType.Set(MediaTypeAttributeKeys.FrameRate, PackUint64((uint)frNum, (uint)frDen));
        outType.Set(MediaTypeAttributeKeys.PixelAspectRatio, PackUint64(1, 1));

        // MF_MT_MPEG2_PROFILE is reused by MF for both H.264 (eAVEncH264VProfile) and
        // HEVC (eAVEncH265VProfile). HDR → force Main10; otherwise honor user selection.
        if (useHevcMain10)
        {
            int hevcProfile = isHdr
                ? (int)MFVideoEncoderSettings.HevcProfileType.Main10
                : (int)VideoSettings.HevcProfile;
            outType.Set(MediaTypeAttributeKeys.Mpeg2Profile, hevcProfile);
        }
        else
        {
            outType.Set(MediaTypeAttributeKeys.Mpeg2Profile, (int)VideoSettings.H264Profile);
        }

        // Set color tags on the output media type so downstream demuxers/players
        // can reproduce the intended grade — this is what tells HDR TVs to switch
        // modes, and SDR viewers to apply BT.709 instead of defaulting to sRGB.
        ApplyColorTags(outType, isHdr);

        int streamIndex = sinkWriter.AddStream(outType);

        // Input (uncompressed) media type: P010 for HDR, NV12 for SDR.
        using IMFMediaType inType = MediaFactory.MFCreateMediaType();
        inType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
        inType.Set(MediaTypeAttributeKeys.Subtype, isHdr ? VideoFormatGuids.P010 : VideoFormatGuids.NV12);
        inType.Set(MediaTypeAttributeKeys.InterlaceMode, 2u); // Progressive
        inType.Set(MediaTypeAttributeKeys.FrameSize, PackUint64((uint)width, (uint)height));
        inType.Set(MediaTypeAttributeKeys.FrameRate, PackUint64((uint)frNum, (uint)frDen));
        inType.Set(MediaTypeAttributeKeys.PixelAspectRatio, PackUint64(1, 1));
        ApplyColorTags(inType, isHdr);

        sinkWriter.SetInputMediaType(streamIndex, inType, null);
        return streamIndex;
    }

    private void ApplyColorTags(IMFMediaType mediaType, bool isHdr)
    {
        // Defaults fire in when the user picks "Default" in the UI: for HDR we need
        // Rec.2020/PQ (HDR10) unconditionally; for SDR we leave the field unset so
        // the decoder picks its own default (typically BT.709 for HD, BT.601 for SD).
        VideoTransferFunction trc = MapTransferToMF(VideoSettings.ColorTransfer, isHdr);
        VideoPrimaries primaries = MapPrimariesToMF(VideoSettings.ColorPrimaries, isHdr);
        VideoTransferMatrix matrix = MapMatrixToMF(VideoSettings.YCbCrMatrix, isHdr);

        if (trc != VideoTransferFunction.FuncUnknown)
            mediaType.Set(MediaTypeAttributeKeys.TransferFunction, (uint)trc);
        if (primaries != VideoPrimaries.Unknown)
            mediaType.Set(MediaTypeAttributeKeys.VideoPrimaries, (uint)primaries);
        if (matrix != VideoTransferMatrix.Unknown)
            mediaType.Set(MediaTypeAttributeKeys.YuvMatrix, (uint)matrix);
    }

    // -------------------- Audio stream setup --------------------

    // The renderer always hands us Pcm<Stereo32BitFloat>. We can faithfully emit mono
    // (downmix L+R) or stereo (passthrough); anything wider than 2 has no source data
    // to fill it, so clamping is safer than declaring a layout we cannot populate.
    private int ResolveAudioChannels()
    {
        int requested = AudioSettings.Channels;
        if (requested <= 0) return 2;
        if (requested >= 2) return 2;
        return 1;
    }

    private static bool IsAudioOnlyContainer(string outputFile)
    {
        string ext = Path.GetExtension(outputFile);
        return ext.Equals(".m4a", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".wav", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".mp3", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".aac", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".adts", StringComparison.OrdinalIgnoreCase);
    }

    private int ConfigureAudioStream(IMFSinkWriter sinkWriter, long sourceSampleRate, int channels)
    {
        int sampleRate = AudioSettings.SampleRate > 0 ? AudioSettings.SampleRate : (int)sourceSampleRate;
        if (sampleRate <= 0)
        {
            return -1;
        }

        using IMFMediaType outType = MediaFactory.MFCreateMediaType();
        outType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Audio);

        switch (AudioSettings.Codec)
        {
            case MFAudioEncoderSettings.AudioCodecType.MP3:
                outType.Set(MediaTypeAttributeKeys.Subtype, AudioFormatGuids.Mp3);
                outType.Set(MediaTypeAttributeKeys.AudioAvgBytesPerSecond, AudioSettings.Bitrate / 8);
                break;
            case MFAudioEncoderSettings.AudioCodecType.WMA:
                outType.Set(MediaTypeAttributeKeys.Subtype, AudioFormatGuids.WMAudioV9);
                outType.Set(MediaTypeAttributeKeys.AudioAvgBytesPerSecond, AudioSettings.Bitrate / 8);
                break;
            case MFAudioEncoderSettings.AudioCodecType.PCM:
                // Uncompressed 16-bit little-endian — no bitrate field needed.
                outType.Set(MediaTypeAttributeKeys.Subtype, AudioFormatGuids.Pcm);
                outType.Set(MediaTypeAttributeKeys.AudioBitsPerSample, 16);
                outType.Set(MediaTypeAttributeKeys.AudioBlockAlignment, (uint)(channels * 2));
                outType.Set(MediaTypeAttributeKeys.AudioAvgBytesPerSecond, (uint)(sampleRate * channels * 2));
                break;
            default: // AAC
                outType.Set(MediaTypeAttributeKeys.Subtype, AudioFormatGuids.Aac);
                outType.Set(MediaTypeAttributeKeys.AudioBitsPerSample, 16);
                outType.Set(MediaTypeAttributeKeys.AudioAvgBytesPerSecond, AudioSettings.Bitrate / 8);
                break;
        }

        outType.Set(MediaTypeAttributeKeys.AudioSamplesPerSecond, sampleRate);
        outType.Set(MediaTypeAttributeKeys.AudioNumChannels, channels);

        int streamIndex = sinkWriter.AddStream(outType);

        // Input: 16-bit signed PCM. The MF AAC encoder MFT only supports this input
        // shape — Stereo32BitFloat (our internal working format) must be downmixed.
        using IMFMediaType inType = MediaFactory.MFCreateMediaType();
        inType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Audio);
        inType.Set(MediaTypeAttributeKeys.Subtype, AudioFormatGuids.Pcm);
        inType.Set(MediaTypeAttributeKeys.AudioBitsPerSample, 16);
        inType.Set(MediaTypeAttributeKeys.AudioSamplesPerSecond, sampleRate);
        inType.Set(MediaTypeAttributeKeys.AudioNumChannels, channels);
        inType.Set(MediaTypeAttributeKeys.AudioBlockAlignment, (uint)(channels * 2));
        inType.Set(MediaTypeAttributeKeys.AudioAvgBytesPerSecond, (uint)(sampleRate * channels * 2));

        sinkWriter.SetInputMediaType(streamIndex, inType, null);
        return streamIndex;
    }

    // -------------------- Frame writing --------------------

    private static async ValueTask WriteVideoFrame(
        IMFSinkWriter sinkWriter, int streamIndex,
        IFrameProvider frameProvider,
        long frame, long frameRateNum, long frameRateDen,
        BitmapColorSpace? hdrTargetColorSpace, bool isHdr)
    {
        using Bitmap image = await frameProvider.RenderFrame(frame);

        long ptsHns = frameRateNum > 0
            ? frame * frameRateDen * HnsPerSecond / frameRateNum
            : 0;
        long durationHns = frameRateNum > 0
            ? frameRateDen * HnsPerSecond / frameRateNum
            : HnsPerSecond / 30;

        if (isHdr && hdrTargetColorSpace is not null)
        {
            // 16-bit RGBA in HDR target color space → P010 (10-bit YUV 4:2:0 MSB-aligned).
            using Bitmap converted = image.Convert(
                BitmapColorType.Rgba16161616, BitmapAlphaType.Unpremul, hdrTargetColorSpace);
            WriteVideoSample(sinkWriter, streamIndex, converted,
                BitmapColorType.Rgba16161616, ptsHns, durationHns);
        }
        else
        {
            using Bitmap converted = image.Convert(
                BitmapColorType.Bgra8888, BitmapAlphaType.Premul, BitmapColorSpace.Srgb);
            WriteVideoSample(sinkWriter, streamIndex, converted,
                BitmapColorType.Bgra8888, ptsHns, durationHns);
        }
    }

    private static unsafe void WriteVideoSample(
        IMFSinkWriter sinkWriter, int streamIndex, Bitmap converted,
        BitmapColorType srcKind, long ptsHns, long durationHns)
    {
        int width = converted.Width;
        int height = converted.Height;

        // P010 stride is width*2 bytes because each 10-bit sample is packed into
        // 16 bits. NV12 uses 1 byte per Y sample.
        bool isP010 = srcKind == BitmapColorType.Rgba16161616;
        int dstStride = isP010 ? width * 2 : width;
        int ySize = dstStride * height;
        int uvHeight = (height + 1) / 2;
        int uvSize = dstStride * uvHeight;
        int totalSize = ySize + uvSize;

        using IMFMediaBuffer buffer = MediaFactory.MFCreateMemoryBuffer(totalSize);
        buffer.Lock(out IntPtr dstPtr, out _, out _);
        byte* dst = (byte*)dstPtr;
        try
        {
            byte* src = (byte*)converted.Data;
            int srcStride = converted.RowBytes;
            if (isP010)
            {
                PixelFormatConverter.Rgba16ToP010(src, srcStride, dst, dstStride, width, height);
            }
            else
            {
                PixelFormatConverter.BgraToNv12(src, srcStride, dst, dstStride, width, height);
            }
        }
        finally
        {
            buffer.Unlock();
        }
        buffer.CurrentLength = totalSize;

        using IMFSample sample = MediaFactory.MFCreateSample();
        sample.AddBuffer(buffer);
        sample.SampleTime = ptsHns;
        sample.SampleDuration = durationHns;
        sinkWriter.WriteSample(streamIndex, sample);
    }

    private static async ValueTask WriteAudioFrame(
        IMFSinkWriter sinkWriter, int streamIndex,
        ISampleProvider sampleProvider,
        long startSample, int length, long sampleRate, int channels)
    {
        using Pcm<Stereo32BitFloat> floatPcm = await sampleProvider.Sample(startSample, length);
        int numSamples = floatPcm.NumSamples;
        int byteCount = numSamples * channels * 2; // int16

        using IMFMediaBuffer buffer = MediaFactory.MFCreateMemoryBuffer(byteCount);
        buffer.Lock(out IntPtr dstPtr, out _, out _);
        unsafe
        {
            short* dst = (short*)dstPtr;
            try
            {
                ReadOnlySpan<Stereo32BitFloat> src = floatPcm.DataSpan;
                if (channels == 1)
                {
                    // L+R averaged into a single channel keeps both sides audible without
                    // doubling perceived amplitude.
                    for (int i = 0; i < src.Length; i++)
                    {
                        float mono = (src[i].Left + src[i].Right) * 0.5f;
                        dst[i] = FloatToPcm16(mono);
                    }
                }
                else
                {
                    for (int i = 0; i < src.Length; i++)
                    {
                        dst[i * 2 + 0] = FloatToPcm16(src[i].Left);
                        dst[i * 2 + 1] = FloatToPcm16(src[i].Right);
                    }
                }
            }
            finally
            {
                buffer.Unlock();
            }
        }
        buffer.CurrentLength = byteCount;

        long ptsHns = sampleRate > 0 ? startSample * HnsPerSecond / sampleRate : 0;
        long durationHns = sampleRate > 0 ? (long)numSamples * HnsPerSecond / sampleRate : 0;

        using IMFSample sample = MediaFactory.MFCreateSample();
        sample.AddBuffer(buffer);
        sample.SampleTime = ptsHns;
        sample.SampleDuration = durationHns;
        sinkWriter.WriteSample(streamIndex, sample);
    }

    private static short FloatToPcm16(float sample)
    {
        // Use 32767 (not 32768) for the positive side to avoid clipping +1.0 into
        // a wraparound. Symmetric clamp keeps the DC bias out of the output.
        float scaled = sample * 32767f;
        if (scaled > 32767f) scaled = 32767f;
        else if (scaled < -32768f) scaled = -32768f;
        return (short)scaled;
    }

    private static ulong PackUint64(uint hi, uint lo) => ((ulong)hi << 32) | lo;

    // -------------------- Enum mapping helpers --------------------
    // The helpers below drop into MFColorSpaceHelper's domain (for Skia color-space
    // construction) and into Media Foundation's tag domain (for the encoder stream
    // attributes). Keeping both mappings in this file means the encoder never has
    // to cross-cast through a third enum representation.

    private static VideoTransferFunction MapTransferToMF(
        MFVideoEncoderSettings.ColorTransferCharacteristic t, bool isHdr)
    {
        // HDR must have a valid transfer tag — default to PQ (HDR10) when the user
        // left the field unset. SDR leaves it Unknown (decoder default kicks in).
        if (t == MFVideoEncoderSettings.ColorTransferCharacteristic.Default)
            return isHdr ? VideoTransferFunction.Func2084 : VideoTransferFunction.FuncUnknown;

        return t switch
        {
            MFVideoEncoderSettings.ColorTransferCharacteristic.Srgb => VideoTransferFunction.FuncSRGB,
            MFVideoEncoderSettings.ColorTransferCharacteristic.Linear => VideoTransferFunction.Func10,
            MFVideoEncoderSettings.ColorTransferCharacteristic.Bt709 => VideoTransferFunction.Func709,
            MFVideoEncoderSettings.ColorTransferCharacteristic.Pq => VideoTransferFunction.Func2084,
            MFVideoEncoderSettings.ColorTransferCharacteristic.Hlg => VideoTransferFunction.FuncHlg,
            MFVideoEncoderSettings.ColorTransferCharacteristic.Smpte240M => VideoTransferFunction.Func240m,
            _ => VideoTransferFunction.FuncUnknown,
        };
    }

    private static VideoPrimaries MapPrimariesToMF(
        MFVideoEncoderSettings.ColorPrimariesType p, bool isHdr)
    {
        if (p == MFVideoEncoderSettings.ColorPrimariesType.Default)
            return isHdr ? VideoPrimaries.Bt2020 : VideoPrimaries.Unknown;

        return p switch
        {
            MFVideoEncoderSettings.ColorPrimariesType.Bt709 => VideoPrimaries.Bt709,
            MFVideoEncoderSettings.ColorPrimariesType.Rec2020 => VideoPrimaries.Bt2020,
            MFVideoEncoderSettings.ColorPrimariesType.Dcip3 => VideoPrimaries.DciP3,
            MFVideoEncoderSettings.ColorPrimariesType.Smpte170M => VideoPrimaries.Smpte170m,
            _ => VideoPrimaries.Unknown,
        };
    }

    private static VideoTransferMatrix MapMatrixToMF(
        MFVideoEncoderSettings.YCbCrMatrixType m, bool isHdr)
    {
        if (m == MFVideoEncoderSettings.YCbCrMatrixType.Default)
            return isHdr ? VideoTransferMatrix.Bt202010 : VideoTransferMatrix.Unknown;

        return m switch
        {
            MFVideoEncoderSettings.YCbCrMatrixType.Bt709 => VideoTransferMatrix.Bt709,
            MFVideoEncoderSettings.YCbCrMatrixType.Bt601 => VideoTransferMatrix.Bt601,
            MFVideoEncoderSettings.YCbCrMatrixType.Rec2020 => VideoTransferMatrix.Bt202010,
            MFVideoEncoderSettings.YCbCrMatrixType.Smpte240M => VideoTransferMatrix.Smpte240m,
            _ => VideoTransferMatrix.Unknown,
        };
    }

    private static VideoTransferFunction MapTransferForHelper(
        MFVideoEncoderSettings.ColorTransferCharacteristic t)
    {
        return t switch
        {
            MFVideoEncoderSettings.ColorTransferCharacteristic.Pq => VideoTransferFunction.Func2084,
            MFVideoEncoderSettings.ColorTransferCharacteristic.Hlg => VideoTransferFunction.FuncHlg,
            MFVideoEncoderSettings.ColorTransferCharacteristic.Bt709 => VideoTransferFunction.Func709,
            MFVideoEncoderSettings.ColorTransferCharacteristic.Srgb => VideoTransferFunction.FuncSRGB,
            MFVideoEncoderSettings.ColorTransferCharacteristic.Linear => VideoTransferFunction.Func10,
            MFVideoEncoderSettings.ColorTransferCharacteristic.Smpte240M => VideoTransferFunction.Func240m,
            _ => VideoTransferFunction.Func2084, // HDR default
        };
    }

    private static VideoPrimaries MapPrimariesForHelper(
        MFVideoEncoderSettings.ColorPrimariesType p)
    {
        return p switch
        {
            MFVideoEncoderSettings.ColorPrimariesType.Bt709 => VideoPrimaries.Bt709,
            MFVideoEncoderSettings.ColorPrimariesType.Rec2020 => VideoPrimaries.Bt2020,
            MFVideoEncoderSettings.ColorPrimariesType.Dcip3 => VideoPrimaries.DciP3,
            MFVideoEncoderSettings.ColorPrimariesType.Smpte170M => VideoPrimaries.Smpte170m,
            _ => VideoPrimaries.Bt2020, // HDR default
        };
    }
}
