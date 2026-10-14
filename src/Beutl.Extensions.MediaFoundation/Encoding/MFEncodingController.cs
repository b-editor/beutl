using System.Runtime.InteropServices;
using System.Runtime.Versioning;
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
        => MFEncodingFormat.FromFile(OutputFile).VideoCodecs.Length > 0 && VideoSettings.Codec != MFVideoCodec.None;

    public override bool IsAudioEnabled
        => MFEncodingFormat.FromFile(OutputFile).ResolveAudioCodec(AudioSettings.Codec) != MFAudioCodec.None;

    public override async ValueTask Encode(
        IFrameProvider frameProvider, ISampleProvider sampleProvider, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Media Foundation encoding requires Windows.");

        var format = MFEncodingFormat.FromFile(OutputFile);
        var videoCodec = format.ResolveVideoCodec(VideoSettings.Codec);
        var audioCodec = format.ResolveAudioCodec(AudioSettings.Codec);
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
        if (frameCount > 0 && !IsVideoEncoderAvailable(videoCodec, VideoSettings.UseHardwareAcceleration))
            throw new NotSupportedException($"No Media Foundation {videoCodec} video encoder is available with the current hardware acceleration setting. Enable hardware acceleration or install a compatible encoder.");
        // Negotiate audio before opening the file so an unsupported format cannot truncate it.
        using var audioType = sampleCount > 0 ? CreateAudioOutput(audioCodec, AudioSettings) : null;
        cancellationToken.ThrowIfCancellationRequested();
        using var attributes = MediaFactory.MFCreateAttributes(2);
        attributes.Set(TranscodeAttributeKeys.TranscodeContainertype, format.ContainerType).CheckError();
        attributes.Set(SinkWriterAttributeKeys.ReadwriteEnableHardwareTransforms,
            VideoSettings.UseHardwareAcceleration).CheckError();
        using var writer = MediaFactory.MFCreateSinkWriterFromURL(outputFile, null, attributes);

        int videoStream = frameCount > 0 ? AddVideoStream(writer, videoCodec) : -1;
        int audioStream = audioType != null ? AddAudioStream(writer, audioType, checked((int)samples.SampleRate)) : -1;
        writer.BeginWriting();

        long frame = 0;
        long sample = 0;
        long rateNumerator = VideoSettings.FrameRate.Numerator;
        long rateDenominator = VideoSettings.FrameRate.Denominator;
        while (frame < frameCount || sample < sampleCount)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long videoTime = videoStream >= 0 ? GetTimestamp(frame, rateNumerator, rateDenominator) : long.MaxValue;
            long audioTime = audioStream >= 0 ? GetTimestamp(sample, samples.SampleRate) : long.MaxValue;
            if (frame < frameCount && (sample >= sampleCount || videoTime <= audioTime))
            {
                using var bitmap = await frames.RenderFrame(frame);
                cancellationToken.ThrowIfCancellationRequested();
                WriteVideoFrame(writer, videoStream, bitmap, videoTime,
                    GetTimestamp(frame + 1, rateNumerator, rateDenominator) - videoTime);
                frame++;
            }
            else
            {
                int length = (int)Math.Min(AudioBlockSize, sampleCount - sample);
                using var pcm = await samples.Sample(sample, length);
                cancellationToken.ThrowIfCancellationRequested();
                if (pcm.NumSamples != length || pcm.SampleRate != samples.SampleRate)
                    throw new InvalidOperationException("The audio provider returned an unexpected sample count or sample rate.");

                WriteSample(writer, audioStream, pcm.Data, checked(length * 2 * sizeof(float)), audioTime,
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

    private int AddVideoStream(IMFSinkWriter writer, MFVideoCodec codec)
    {
        using var outputType = CreateVideoType(MFEncodingFormat.VideoSubtype(codec));
        if (codec != MFVideoCodec.RGB32)
            outputType.Set(MediaTypeAttributeKeys.AvgBitrate, (uint)VideoSettings.Bitrate).CheckError();
        int stream = writer.AddStream(outputType);

        using var inputType = CreateVideoType(VideoFormatGuids.Rgb32);
        using var parameters = MediaFactory.MFCreateAttributes(1);
        if (codec is MFVideoCodec.H264 or MFVideoCodec.HEVC)
        {
            parameters.Set(typeof(CODECAPI_AVEncMPVGOPSize).GUID, (uint)VideoSettings.KeyframeRate).CheckError();
        }
        else if (codec is MFVideoCodec.WMV7 or MFVideoCodec.WMV8 or MFVideoCodec.WMV9 or MFVideoCodec.VC1)
        {
            Marshal.ThrowExceptionForHR(PSCreateMemoryPropertyStore(typeof(IPropertyStore).GUID, out nint pointer));
            using var properties = new IPropertyStore(pointer);
            // MFPKEY_KEYDIST (wmcodecdsp.h) is absent from the Win32 metadata.
            var key = new PropertyKey(new Guid("18d6f8c5-2416-4d7b-90d7-9f3f21e752b4"), 10);
            // Windows Media expects milliseconds, while KeyframeRate is measured in frames.
            int milliseconds = checked((int)Math.Max(1,
                GetTimestamp(VideoSettings.KeyframeRate, VideoSettings.FrameRate.Numerator,
                    VideoSettings.FrameRate.Denominator) / TimeSpan.TicksPerMillisecond));
            properties[key] = new Variant { Value = milliseconds };
            // Scope this to the video stream: WMA rejects video encoder properties.
            parameters.Set(SinkWriterAttributeKeys.EncoderConfig, properties).CheckError();
        }
        writer.SetInputMediaType(stream, inputType, parameters);
        return stream;
    }

    [DllImport("propsys.dll", ExactSpelling = true)]
    private static extern int PSCreateMemoryPropertyStore(in Guid riid, out nint propertyStore);

    private IMFMediaType CreateVideoType(Guid subtype)
    {
        var type = MediaFactory.MFCreateMediaType();
        try
        {
            type.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video).CheckError();
            type.Set(MediaTypeAttributeKeys.Subtype, subtype).CheckError();
            type.Set(MediaTypeAttributeKeys.FrameSize,
                Pack(VideoSettings.DestinationSize.Width, VideoSettings.DestinationSize.Height)).CheckError();
            type.Set(MediaTypeAttributeKeys.FrameRate,
                Pack(VideoSettings.FrameRate.Numerator, VideoSettings.FrameRate.Denominator)).CheckError();
            type.Set(MediaTypeAttributeKeys.PixelAspectRatio, Pack(1, 1)).CheckError();
            type.Set(MediaTypeAttributeKeys.InterlaceMode, (uint)VideoInterlaceMode.Progressive).CheckError();
            if (subtype == VideoFormatGuids.Rgb32)
                type.Set(MediaTypeAttributeKeys.DefaultStride, checked((uint)VideoSettings.DestinationSize.Width * 4)).CheckError();
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
            int bits = codec == MFAudioCodec.Float ? 32 : (int)settings.BitDepth;
            return CreatePcm(subtype, settings.SampleRate, settings.Channels, bits);
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
                    if (candidate.GetUInt32(MediaTypeAttributeKeys.AudioSamplesPerSecond) != settings.SampleRate
                        || candidate.GetUInt32(MediaTypeAttributeKeys.AudioNumChannels) != settings.Channels)
                        continue;

                    uint bits = MediaFactory.MFGetAttributeUInt32(candidate, MediaTypeAttributeKeys.AudioBitsPerSample);
                    if (lossless ? bits != (int)settings.BitDepth : bits is not (0 or 16))
                        continue;

                    // WMA bitrates include codec overhead (e.g. 128016 for a requested 128000).
                    // Preserve the full enumerated type, including required codec-specific data.
                    long distance = lossless ? 0 : Math.Abs(
                        (long)candidate.GetUInt32(MediaTypeAttributeKeys.AudioAvgBytesPerSecond) * 8 - settings.Bitrate);
                    if (distance >= bestDistance)
                        continue;

                    best?.Dispose();
                    best = candidate.QueryInterface<IMFMediaType>();
                    bestDistance = distance;
                }

                return best ?? throw new NotSupportedException(
                    $"The installed {codec} encoder does not support {settings.SampleRate} Hz, {settings.Channels} channel(s)"
                    + (lossless ? $", {(int)settings.BitDepth}-bit audio." : "."));
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

    private void WriteVideoFrame(IMFSinkWriter writer, int stream, Bitmap bitmap, long time, long duration)
    {
        using var converted = bitmap.Convert(BitmapColorType.Bgra8888, BitmapAlphaType.Premul, BitmapColorSpace.Srgb);
        using var source = new SKBitmap();
        var sourceInfo = new SKImageInfo(converted.Width, converted.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        if (!source.InstallPixels(sourceInfo, converted.Data, converted.RowBytes))
            throw new InvalidOperationException("Could not access the rendered frame.");

        var size = VideoSettings.DestinationSize;
        using var scaled = new SKBitmap(size.Width, size.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using (var canvas = new SKCanvas(scaled))
        {
            canvas.Clear(SKColors.Black);
            canvas.DrawBitmap(source, new SKRect(0, 0, size.Width, size.Height),
                new SKSamplingOptions(SKFilterMode.Linear));
        }

        int rowBytes = checked(size.Width * 4);
        using var buffer = MediaFactory.MFCreateMemoryBuffer(checked(rowBytes * size.Height));
        buffer.Lock(out nint destination, out _, out _);
        try
        {
            unsafe
            {
                for (int y = 0; y < size.Height; y++)
                    Buffer.MemoryCopy((byte*)scaled.GetPixels() + y * scaled.RowBytes,
                        (byte*)destination + y * rowBytes, rowBytes, rowBytes);
            }
        }
        finally
        {
            buffer.Unlock();
        }
        buffer.CurrentLength = checked(rowBytes * size.Height);
        WriteSample(writer, stream, buffer, time, duration);
    }

    private static unsafe void WriteSample(IMFSinkWriter writer, int stream, nint data, int length, long time, long duration)
    {
        using var buffer = MediaFactory.MFCreateMemoryBuffer(length);
        buffer.Lock(out nint destination, out _, out _);
        try
        {
            Buffer.MemoryCopy((void*)data, (void*)destination, length, length);
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
            var size = VideoSettings.DestinationSize;
            if (size.Width <= 0 || size.Height <= 0
                || (videoCodec != MFVideoCodec.RGB32 && (size.Width % 2 != 0 || size.Height % 2 != 0)))
                throw new ArgumentException("Video output requires positive dimensions; compressed video requires even dimensions.");
            _ = checked(size.Width * size.Height * 4);
            if (VideoSettings.FrameRate.Numerator <= 0 || VideoSettings.FrameRate.Numerator > uint.MaxValue
                || VideoSettings.FrameRate.Denominator <= 0 || VideoSettings.FrameRate.Denominator > uint.MaxValue
                || GetTimestamp(1, VideoSettings.FrameRate.Numerator, VideoSettings.FrameRate.Denominator) == 0)
                throw new ArgumentException("The video frame rate must be positive and representable in Media Foundation.");
            if (videoCodec != MFVideoCodec.RGB32 && (VideoSettings.Bitrate <= 0 || VideoSettings.KeyframeRate <= 0))
                throw new ArgumentException("The video bitrate and keyframe interval must be positive.");
        }

        if (sampleCount > 0)
        {
            if (sourceSampleRate <= 0 || sourceSampleRate > int.MaxValue / 8)
                throw new ArgumentException("The source sample rate must be positive and representable in Media Foundation.");
            if (AudioSettings.SampleRate <= 0 || AudioSettings.SampleRate > int.MaxValue / 8)
                throw new ArgumentException("The output sample rate must be positive and representable in Media Foundation.");
            if (AudioSettings.Channels is not (1 or 2))
                throw new ArgumentException("Media Foundation encoding supports mono or stereo output.");
            if (audioCodec is MFAudioCodec.PCM or MFAudioCodec.FLAC or MFAudioCodec.ALAC)
            {
                if (!Enum.IsDefined(AudioSettings.BitDepth))
                    throw new ArgumentException("The audio bit depth must be 8, 16, 24, or 32 bits.");
            }
            else if (audioCodec != MFAudioCodec.Float && AudioSettings.Bitrate <= 0)
            {
                throw new ArgumentException("The audio bitrate must be positive.");
            }
        }
    }
}
