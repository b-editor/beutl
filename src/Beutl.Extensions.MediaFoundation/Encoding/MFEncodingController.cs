using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Beutl.Audio;
using Beutl.Extensibility;
using Beutl.IO;
using Beutl.Media;
using SharpGen.Runtime;
using SharpGen.Runtime.Win32;
using SkiaSharp;
using Vortice.MediaFoundation;
using Windows.Win32.Media.MediaFoundation;

#if MF_BUILD_IN
using Beutl.Embedding.MediaFoundation.Decoding;
namespace Beutl.Embedding.MediaFoundation.Encoding;
#else
using Beutl.Extensions.MediaFoundation.Decoding;
namespace Beutl.Extensions.MediaFoundation.Encoding;
#endif

[SupportedOSPlatform("windows")]
public sealed class MFEncodingController(string outputFile) : EncodingController(outputFile)
{
    private const int AudioBlockSize = 1024;
    private const EnumFlag SoftwareEncoders = EnumFlag.EnumFlagSyncmft | EnumFlag.EnumFlagAsyncmft
        | EnumFlag.EnumFlagLocalmft | EnumFlag.EnumFlagTranscodeOnly | EnumFlag.EnumFlagSortandfilter;

    public override MFVideoEncoderSettings VideoSettings { get; } = new();

    public override MFAudioEncoderSettings AudioSettings { get; } = new();

    public override bool IsVideoEnabled
        => MFEncodingFormat.FromFile(OutputFile).VideoCodecs.Length > 0 && VideoSettings.Codec.Value != MFVideoCodec.None;

    public override bool IsAudioEnabled
        => MFEncodingFormat.FromFile(OutputFile).ResolveAudioCodec(AudioSettings.Codec.Value) != MFAudioCodec.None;

    public override async ValueTask Encode(
        IFrameProvider frameProvider, ISampleProvider sampleProvider, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Media Foundation encoding requires Windows.");

        var format = MFEncodingFormat.FromFile(OutputFile);
        var videoCodec = format.ResolveVideoCodec(VideoSettings.Codec.Value);
        var audioCodec = format.ResolveAudioCodec(AudioSettings.Codec.Value);
        long frameCount = videoCodec == MFVideoCodec.None ? 0 : frameProvider.FrameCount;
        long sampleCount = audioCodec == MFAudioCodec.None ? 0 : sampleProvider.SampleCount;
        ValidateSettings(format, videoCodec, audioCodec, frameCount, sampleCount, sampleProvider.SampleRate);
        // Product exports already use a fresh staging path. Protect existing files for direct callers too.
        using var output = File.Exists(OutputFile) ? new StagedOutputFile(OutputFile) : null;
        // Keep the writer, its samples, and their disposal on the initialized MF thread.
        // Await the operation itself even on cancellation so the file handle is released before returning.
        await MFThread.Dispatcher.InvokeAsync(() => EncodeCore(frameProvider, sampleProvider,
            output?.TemporaryPath ?? OutputFile, format, videoCodec, audioCodec, frameCount, sampleCount, cancellationToken));
        output?.Commit(cancellationToken);
    }

    private async Task EncodeCore(
        IFrameProvider frames, ISampleProvider samples, string outputFile, MFEncodingFormat format,
        MFVideoCodec videoCodec, MFAudioCodec audioCodec, long frameCount, long sampleCount,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (frameCount > 0 && !IsVideoEncoderAvailable(videoCodec, VideoSettings.UseHardwareAcceleration.Value))
            throw new NotSupportedException($"No Media Foundation {videoCodec} video encoder is available with the current hardware acceleration setting. Enable hardware acceleration or install a compatible encoder.");
        // Negotiate audio before opening the file so an unsupported format cannot truncate it.
        using var audioType = sampleCount > 0 ? CreateAudioOutput(audioCodec, AudioSettings) : null;
        cancellationToken.ThrowIfCancellationRequested();
        using var attributes = MediaFactory.MFCreateAttributes(2);
        attributes.Set(TranscodeAttributeKeys.TranscodeContainertype, format.ContainerType).CheckError();
        attributes.Set(SinkWriterAttributeKeys.ReadwriteEnableHardwareTransforms,
            VideoSettings.UseHardwareAcceleration.Value).CheckError();
        using var writer = MediaFactory.MFCreateSinkWriterFromURL(outputFile, null, attributes);

        // H.264/HEVC carry explicit colorimetry. The legacy Windows Media/RGB path stays untagged.
        VideoColorTags color = VideoColorDefaults.Get(VideoColorTarget.YCbCr, hdr: false,
            recordsColor: videoCodec is MFVideoCodec.H264 or MFVideoCodec.HEVC);
        int videoStream = frameCount > 0 ? AddVideoStream(writer, videoCodec, color) : -1;
        int audioStream = audioType != null ? AddAudioStream(writer, audioType, checked((int)samples.SampleRate)) : -1;
        writer.BeginWriting();

        long frame = 0;
        long sample = 0;
        long rateNumerator = VideoSettings.FrameRate.Value.Numerator;
        long rateDenominator = VideoSettings.FrameRate.Value.Denominator;
        BitmapColorSpace colorSpace = BitmapColorSpaceMapping.BuildTargetColorSpace(color.Transfer, color.Primaries);
        while (frame < frameCount || sample < sampleCount)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long videoTime = videoStream >= 0 ? GetTimestamp(frame, rateNumerator, rateDenominator) : long.MaxValue;
            long audioTime = audioStream >= 0 ? GetTimestamp(sample, samples.SampleRate) : long.MaxValue;
            if (frame < frameCount && (sample >= sampleCount || videoTime <= audioTime))
            {
                using var bitmap = await frames.ReadNextFrame()
                    ?? throw new InvalidOperationException("The frame provider ended before its frame count.");
                cancellationToken.ThrowIfCancellationRequested();
                WriteVideoFrame(writer, videoStream, bitmap, color, colorSpace, videoTime,
                    GetTimestamp(frame + 1, rateNumerator, rateDenominator) - videoTime);
                frame++;
            }
            else
            {
                int length = (int)Math.Min(AudioBlockSize, sampleCount - sample);
                using var audio = await samples.ReadNextSamples(length);
                cancellationToken.ThrowIfCancellationRequested();
                if (audio is null || audio.SampleCount != length || audio.SampleRate != samples.SampleRate)
                    throw new InvalidOperationException("The audio provider returned an unexpected sample count or sample rate.");

                WriteSample(writer, audioStream, audio, audioTime,
                    GetTimestamp(sample + length, samples.SampleRate) - audioTime);
                sample += length;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        // Finalize writes container indexes/headers. A cancelled or failed encode must never report success.
        writer.Finalize();
    }

    internal static bool IsVideoEncoderAvailable(MFVideoCodec codec, bool hardware)
    {
        if (codec == MFVideoCodec.RGB32)
            return true;

        var output = new RegisterTypeInfo
        {
            GuidMajorType = MediaTypeGuids.Video,
            GuidSubtype = MFEncodingFormat.VideoSubtype(codec),
        };
        var flags = SoftwareEncoders | (hardware ? EnumFlag.EnumFlagHardware : 0);
        MediaFactory.MFTEnumEx(TransformCategoryGuids.VideoEncoder, (uint)flags, null, output,
            out nint activations, out uint count);
        try
        {
            return count > 0;
        }
        finally
        {
            // The collection helper does not free the native array in Vortice 3.8.3.
            for (int i = 0; i < count; i++)
                Marshal.Release(Marshal.ReadIntPtr(activations, i * IntPtr.Size));
            Marshal.FreeCoTaskMem(activations);
        }
    }

    private int AddVideoStream(IMFSinkWriter writer, MFVideoCodec codec, VideoColorTags color)
    {
        using var outputType = CreateVideoType(MFEncodingFormat.VideoSubtype(codec), color);
        if (codec != MFVideoCodec.RGB32)
            outputType.Set(MediaTypeAttributeKeys.AvgBitrate, (uint)VideoSettings.Bitrate.Value).CheckError();
        int stream = writer.AddStream(outputType);

        // Sink Writer's implicit RGB converter uses BT.601 even when the H.264 output is tagged BT.709.
        // Supply already-converted YUV so the encoder only compresses it.
        using var inputType = CreateVideoType(
            color.Matrix != VideoColorMatrix.Unknown ? VideoFormatGuids.Iyuv : VideoFormatGuids.Rgb32, color);
        using var parameters = MediaFactory.MFCreateAttributes(1);
        if (codec is MFVideoCodec.H264 or MFVideoCodec.HEVC)
        {
            parameters.Set(typeof(CODECAPI_AVEncMPVGOPSize).GUID, (uint)VideoSettings.KeyframeRate.Value).CheckError();
        }
        else if (codec is MFVideoCodec.WMV7 or MFVideoCodec.WMV8 or MFVideoCodec.WMV9 or MFVideoCodec.VC1)
        {
            Marshal.ThrowExceptionForHR(PSCreateMemoryPropertyStore(typeof(IPropertyStore).GUID, out nint pointer));
            using var properties = new IPropertyStore(pointer);
            // MFPKEY_KEYDIST (wmcodecdsp.h) is absent from the Win32 metadata.
            var key = new PropertyKey(new Guid("18d6f8c5-2416-4d7b-90d7-9f3f21e752b4"), 10);
            // Windows Media expects milliseconds, while KeyframeRate is measured in frames.
            int milliseconds = checked((int)Math.Max(1,
                GetTimestamp(VideoSettings.KeyframeRate.Value, VideoSettings.FrameRate.Value.Numerator,
                    VideoSettings.FrameRate.Value.Denominator) / TimeSpan.TicksPerMillisecond));
            properties[key] = new Variant { Value = milliseconds };
            // Scope this to the video stream: WMA rejects video encoder properties.
            parameters.Set(SinkWriterAttributeKeys.EncoderConfig, properties).CheckError();
        }
        writer.SetInputMediaType(stream, inputType, parameters);
        return stream;
    }

    [DllImport("propsys.dll", ExactSpelling = true)]
    private static extern int PSCreateMemoryPropertyStore(in Guid riid, out nint propertyStore);

    private IMFMediaType CreateVideoType(Guid subtype, VideoColorTags color)
    {
        var type = MediaFactory.MFCreateMediaType();
        try
        {
            type.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video).CheckError();
            type.Set(MediaTypeAttributeKeys.Subtype, subtype).CheckError();
            type.Set(MediaTypeAttributeKeys.FrameSize,
                Pack(VideoSettings.DestinationSize.Value.Width, VideoSettings.DestinationSize.Value.Height)).CheckError();
            type.Set(MediaTypeAttributeKeys.FrameRate,
                Pack(VideoSettings.FrameRate.Value.Numerator, VideoSettings.FrameRate.Value.Denominator)).CheckError();
            type.Set(MediaTypeAttributeKeys.PixelAspectRatio, Pack(1, 1)).CheckError();
            type.Set(MediaTypeAttributeKeys.InterlaceMode, (uint)VideoInterlaceMode.Progressive).CheckError();
            MFColorTags.Apply(type, color);
            if (subtype == VideoFormatGuids.Rgb32)
                type.Set(MediaTypeAttributeKeys.DefaultStride, checked((uint)VideoSettings.DestinationSize.Value.Width * 4)).CheckError();
            else if (subtype == VideoFormatGuids.Iyuv)
                type.Set(MediaTypeAttributeKeys.DefaultStride, (uint)VideoSettings.DestinationSize.Value.Width).CheckError();
            return type;
        }
        catch
        {
            type.Dispose();
            throw;
        }
    }

    private static IMFMediaType CreateAudioOutput(MFAudioCodec codec, MFAudioEncoderSettings settings)
    {
        Guid subtype = MFEncodingFormat.AudioSubtype(codec);
        if (codec is MFAudioCodec.PCM or MFAudioCodec.Float)
        {
            int bits = codec == MFAudioCodec.Float ? 32 : (int)settings.BitDepth.Value;
            return CreatePcm(subtype, settings.SampleRate.Value, settings.Channels.Value, bits);
        }

        var result = MediaFactory.MFTranscodeGetAudioOutputAvailableTypes(subtype, (int)SoftwareEncoders,
            null, out var collection);
        if (result.Failure)
        {
            collection?.Dispose();
            throw new NotSupportedException($"No Media Foundation {codec} audio encoder is available on this system.",
                new SharpGenException(result));
        }

        using (collection)
        {
            IMFMediaType? best = null;
            long bestDistance = long.MaxValue;
            bool lossless = codec is MFAudioCodec.FLAC or MFAudioCodec.ALAC;
            try
            {
                for (int i = 0; i < collection.ElementCount; i++)
                {
                    using var element = collection.GetElement(i);
                    using var candidate = ((ComObject)element).QueryInterface<IMFMediaType>();
                    if (candidate.GetUInt32(MediaTypeAttributeKeys.AudioSamplesPerSecond) != settings.SampleRate.Value
                        || candidate.GetUInt32(MediaTypeAttributeKeys.AudioNumChannels) != settings.Channels.Value)
                        continue;

                    uint bits = MediaFactory.MFGetAttributeUInt32(candidate, MediaTypeAttributeKeys.AudioBitsPerSample);
                    if (lossless ? bits != (int)settings.BitDepth.Value : bits is not (0 or 16))
                        continue;

                    // WMA bitrates include codec overhead (e.g. 128016 for a requested 128000).
                    // Preserve the full enumerated type, including required codec-specific data.
                    long distance = lossless ? 0 : Math.Abs(
                        (long)candidate.GetUInt32(MediaTypeAttributeKeys.AudioAvgBytesPerSecond) * 8 - settings.Bitrate.Value);
                    if (distance >= bestDistance)
                        continue;

                    best?.Dispose();
                    best = candidate.QueryInterface<IMFMediaType>();
                    bestDistance = distance;
                }

                return best ?? throw new NotSupportedException(
                    $"The installed {codec} encoder does not support {settings.SampleRate.Value} Hz, {settings.Channels.Value} channel(s)"
                    + (lossless ? $", {(int)settings.BitDepth.Value}-bit audio." : "."));
            }
            catch
            {
                best?.Dispose();
                throw;
            }
        }
    }

    internal static IMFMediaType CreatePcm(Guid subtype, int rate, int channels, int bits)
    {
        var type = MediaFactory.MFCreateMediaType();
        try
        {
            uint alignment = checked((uint)(channels * bits / 8));
            type.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Audio).CheckError();
            type.Set(MediaTypeAttributeKeys.Subtype, subtype).CheckError();
            type.Set(MediaTypeAttributeKeys.AudioSamplesPerSecond, (uint)rate).CheckError();
            type.Set(MediaTypeAttributeKeys.AudioNumChannels, (uint)channels).CheckError();
            type.Set(MediaTypeAttributeKeys.AudioBitsPerSample, (uint)bits).CheckError();
            type.Set(MediaTypeAttributeKeys.AudioBlockAlignment, alignment).CheckError();
            type.Set(MediaTypeAttributeKeys.AudioAvgBytesPerSecond, checked((uint)rate * alignment)).CheckError();
            return type;
        }
        catch
        {
            type.Dispose();
            throw;
        }
    }

    private static int AddAudioStream(IMFSinkWriter writer, IMFMediaType outputType, int sourceSampleRate)
    {
        int stream = writer.AddStream(outputType);

        // The provider always supplies interleaved stereo float. The sink writer converts this to
        // the encoder's input format, including the requested channel count and sample rate.
        using var inputType = CreatePcm(AudioFormatGuids.Float, sourceSampleRate, 2, 32);
        writer.SetInputMediaType(stream, inputType, null);
        return stream;
    }

    private void WriteVideoFrame(IMFSinkWriter writer, int stream, Bitmap bitmap, VideoColorTags color,
        BitmapColorSpace colorSpace, long time, long duration)
    {
        bool yuv = color.Matrix != VideoColorMatrix.Unknown;
        using var converted = bitmap.Convert(BitmapColorType.Bgra8888, BitmapAlphaType.Premul, colorSpace);
        using var source = new SKBitmap();
        var sourceInfo = new SKImageInfo(converted.Width, converted.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        if (!source.InstallPixels(sourceInfo, converted.Data, converted.RowBytes))
            throw new InvalidOperationException("Could not access the rendered frame.");

        var size = VideoSettings.DestinationSize.Value;
        using var scaled = new SKBitmap(size.Width, size.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using (var canvas = new SKCanvas(scaled))
        {
            canvas.Clear(SKColors.Black);
            canvas.DrawBitmap(source, new SKRect(0, 0, size.Width, size.Height),
                new SKSamplingOptions(SKFilterMode.Linear));
        }

        int rowBytes = checked(size.Width * 4);
        int bufferSize = yuv ? YuvConversion.GetI420BufferSize(size.Width, size.Height) : checked(rowBytes * size.Height);
        using var buffer = MediaFactory.MFCreateMemoryBuffer(bufferSize);
        buffer.Lock(out nint destination, out _, out _);
        try
        {
            unsafe
            {
                if (yuv)
                    YuvConversion.BgraToI420((byte*)scaled.GetPixels(), scaled.RowBytes, (byte*)destination,
                        size.Width, size.Height, bt709: color.Matrix == VideoColorMatrix.Bt709);
                else
                    for (int y = 0; y < size.Height; y++)
                        Buffer.MemoryCopy((byte*)scaled.GetPixels() + y * scaled.RowBytes,
                            (byte*)destination + y * rowBytes, rowBytes, rowBytes);
            }
        }
        finally
        {
            buffer.Unlock();
        }
        buffer.CurrentLength = bufferSize;
        WriteSample(writer, stream, buffer, time, duration);
    }

    private static unsafe void WriteSample(IMFSinkWriter writer, int stream, AudioBuffer audio, long time, long duration)
    {
        int length = checked(audio.SampleCount * 2 * sizeof(float));
        using var buffer = MediaFactory.MFCreateMemoryBuffer(length);
        buffer.Lock(out nint destination, out _, out _);
        try
        {
            AudioConversion.Interleave(audio, new Span<float>((void*)destination, audio.SampleCount * 2));
        }
        finally
        {
            buffer.Unlock();
        }
        buffer.CurrentLength = length;
        WriteSample(writer, stream, buffer, time, duration);
    }

    private static void WriteSample(IMFSinkWriter writer, int stream, IMFMediaBuffer buffer, long time, long duration)
    {
        using var sample = MediaFactory.MFCreateSample();
        sample.AddBuffer(buffer);
        sample.SampleTime = time;
        sample.SampleDuration = duration;
        writer.WriteSample(stream, sample);
    }

    // Compute each timestamp from the absolute position; adding rounded frame durations drifts at 29.97 fps.
    internal static long GetTimestamp(long position, long numerator, long denominator = 1)
        => checked((long)((Int128)position * TimeSpan.TicksPerSecond * denominator / numerator));

    private static ulong Pack(long high, long low) => ((ulong)high << 32) | (uint)low;

    private void ValidateSettings(MFEncodingFormat format, MFVideoCodec videoCodec, MFAudioCodec audioCodec,
        long frameCount, long sampleCount, long sourceSampleRate)
    {
        if (frameCount < 0 || sampleCount < 0 || (frameCount == 0 && sampleCount == 0))
            throw new ArgumentException("At least one enabled video frame or audio sample is required for this output format.");
        if (format.RequiresVideo && frameCount == 0)
            throw new ArgumentException("AVI output requires a video stream. Use WAV for PCM audio-only output.");

        if (frameCount > 0)
        {
            var size = VideoSettings.DestinationSize.Value;
            if (size.Width <= 0 || size.Height <= 0
                || (videoCodec != MFVideoCodec.RGB32 && (size.Width % 2 != 0 || size.Height % 2 != 0)))
                throw new ArgumentException("Video output requires positive dimensions; compressed video requires even dimensions.");
            _ = checked(size.Width * size.Height * 4);
            if (VideoSettings.FrameRate.Value.Numerator <= 0 || VideoSettings.FrameRate.Value.Numerator > uint.MaxValue
                || VideoSettings.FrameRate.Value.Denominator <= 0 || VideoSettings.FrameRate.Value.Denominator > uint.MaxValue
                || GetTimestamp(1, VideoSettings.FrameRate.Value.Numerator, VideoSettings.FrameRate.Value.Denominator) == 0)
                throw new ArgumentException("The video frame rate must be positive and representable in Media Foundation.");
            if (videoCodec != MFVideoCodec.RGB32 && (VideoSettings.Bitrate.Value <= 0 || VideoSettings.KeyframeRate.Value <= 0))
                throw new ArgumentException("The video bitrate and keyframe interval must be positive.");
        }

        if (sampleCount > 0)
        {
            if (sourceSampleRate <= 0 || sourceSampleRate > int.MaxValue / 8)
                throw new ArgumentException("The source sample rate must be positive and representable in Media Foundation.");
            if (AudioSettings.SampleRate.Value <= 0 || AudioSettings.SampleRate.Value > int.MaxValue / 8)
                throw new ArgumentException("The output sample rate must be positive and representable in Media Foundation.");
            if (AudioSettings.Channels.Value is not (1 or 2))
                throw new ArgumentException("Media Foundation encoding supports mono or stereo output.");
            if (audioCodec is MFAudioCodec.PCM or MFAudioCodec.FLAC or MFAudioCodec.ALAC)
            {
                if (!Enum.IsDefined(AudioSettings.BitDepth.Value))
                    throw new ArgumentException("The audio bit depth must be 8, 16, 24, or 32 bits.");
            }
            else if (audioCodec != MFAudioCodec.Float && AudioSettings.Bitrate.Value <= 0)
            {
                throw new ArgumentException("The audio bitrate must be positive.");
            }
        }
    }
}
