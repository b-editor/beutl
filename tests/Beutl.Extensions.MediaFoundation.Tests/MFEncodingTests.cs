using System.Runtime.InteropServices;
using Beutl.Audio;
using Beutl.Embedding.MediaFoundation.Decoding;
using Beutl.Embedding.MediaFoundation.Encoding;
using Beutl.Extensibility;
using Beutl.Media;
using Beutl.Media.Decoding;
using Vortice.MediaFoundation;

namespace Beutl.Extensions.MediaFoundation.Tests;

[TestFixture]
[Platform("Win")]
public class MFEncodingTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "beutl-mf-encoding-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, recursive: true);

    [TestCase(false, 192)]
    [TestCase(true, 192)]
    [TestCase(false, 194)]
    public async Task Encode_H264PixelsMatchBt709TransferMatrixAndLimitedRange(bool hardware, int width)
    {
        var controller = CreateController();
        controller.VideoSettings.UseHardwareAcceleration.Value = hardware;
        controller.VideoSettings.SourceSize.Value = new PixelSize(ColorBarsFrameProvider.Width, ColorBarsFrameProvider.Height);
        controller.VideoSettings.DestinationSize.Value = new PixelSize(width, ColorBarsFrameProvider.Height);
        using var frames = new ColorBarsFrameProvider();
        using var samples = new TrackedSamples(0, 0);
        await controller.Encode(frames, samples, CancellationToken.None);

        MFThread.Dispatcher.Invoke(() =>
        {
            using var source = MediaFactory.MFCreateSourceReaderFromURL(controller.OutputFile, null);
            int stream = (int)SourceReaderIndex.FirstVideoStream;
            source.SetStreamSelection((int)SourceReaderIndex.AllStreams, false);
            source.SetStreamSelection(stream, true);
            using var type = MediaFactory.MFCreateMediaType();
            type.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video).CheckError();
            // Decode directly to YUV so an RGB converter cannot hide a wrong encoded matrix or range.
            type.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.NV12).CheckError();
            source.SetCurrentMediaType(stream, type);
            using var sample = source.ReadSample(stream, SourceReaderControlFlag.None, out _, out _, out _);
            Assert.That(sample, Is.Not.Null);
            // The first decode can change the output type to the macroblock-aligned coded size.
            using var current = source.GetCurrentMediaType(stream);
            ulong frameSize = current.GetUInt64(MediaTypeAttributeKeys.FrameSize);
            int decodedHeight = (int)(uint)frameSize;
            int stride = (int)MediaFactory.MFGetAttributeUInt32(current, MediaTypeAttributeKeys.DefaultStride, (uint)(frameSize >> 32));
            using var buffer = sample.ConvertToContiguousBuffer();
            byte[] bytes = CopyBuffer(buffer);

            // Red/green/blue, sRGB gray 128 converted to BT.709, black and white.
            int[][] expected = [[63, 102, 240], [173, 42, 26], [32, 240, 118], [115, 128, 128], [16, 128, 128], [235, 128, 128]];
            Assert.Multiple(() =>
            {
                for (int patch = 0; patch < expected.Length; patch++)
                {
                    int x = (patch * 32 + 16) * width / ColorBarsFrameProvider.Width;
                    int chroma = stride * decodedHeight + 16 * stride + (x & ~1);
                    Assert.That(bytes[32 * stride + x], Is.EqualTo(expected[patch][0]).Within(3), $"Patch {patch}, Y");
                    Assert.That(bytes[chroma], Is.EqualTo(expected[patch][1]).Within(3), $"Patch {patch}, U");
                    Assert.That(bytes[chroma + 1], Is.EqualTo(expected[patch][2]).Within(3), $"Patch {patch}, V");
                }
            });
        });
    }

    [TestCase(false, 44100, 2)]
    [TestCase(false, 48000, 1)]
    [TestCase(true, 48000, 2)]
    public async Task Encode_RoundTripsScaledVideoAndConvertedAudio(bool hardware, int sampleRate, int channels)
    {
        var controller = CreateController();
        controller.VideoSettings.UseHardwareAcceleration.Value = hardware;
        controller.VideoSettings.DestinationSize.Value = new PixelSize(128, 96);
        controller.AudioSettings.SampleRate.Value = sampleRate;
        controller.AudioSettings.Channels.Value = channels;
        File.WriteAllText(controller.OutputFile, "replace only after encoding succeeds");
        using var frames = new GradientFrameProvider(30, new Rational(30, 1), 64, 48);
        // Deliberately differs from the output rate, and ends in a partial 1024-sample block.
        using var samples = new TrackedSamples(44100 + 137, 44100);
        await controller.Encode(frames, samples, CancellationToken.None);

        Assert.That(Directory.GetDirectories(_directory), Is.Empty);
        Assert.That(samples.WasDisposed, Is.False, "Providers belong to the caller.");
        Assert.That(samples.RequestedSamples, Is.EqualTo(samples.SampleCount));
        MFThread.Dispatcher.Invoke(() =>
        {
            using var source = MediaFactory.MFCreateSourceReaderFromURL(controller.OutputFile, null);
            using var video = source.GetNativeMediaType((int)SourceReaderIndex.FirstVideoStream, 0);
            using var audio = source.GetNativeMediaType((int)SourceReaderIndex.FirstAudioStream, 0);
            Assert.That(video.GetGUID(MediaTypeAttributeKeys.Subtype), Is.EqualTo(VideoFormatGuids.H264));
            Assert.That(video.GetUInt32(MediaTypeAttributeKeys.VideoPrimaries), Is.EqualTo((uint)VideoPrimaries.Bt709));
            Assert.That(video.GetUInt32(MediaTypeAttributeKeys.TransferFunction), Is.EqualTo((uint)VideoTransferFunction.Func709));
            Assert.That(video.GetUInt32(MediaTypeAttributeKeys.YuvMatrix), Is.EqualTo((uint)VideoTransferMatrix.Bt709));
            Assert.That(video.GetUInt32(MediaTypeAttributeKeys.VideoNominalRange), Is.EqualTo((uint)NominalRange.Range16_235));
            Assert.That(audio.GetGUID(MediaTypeAttributeKeys.Subtype), Is.EqualTo(AudioFormatGuids.Aac));
            Assert.That(audio.GetUInt32(MediaTypeAttributeKeys.AudioNumChannels), Is.EqualTo(channels));
            Assert.That(audio.GetUInt32(MediaTypeAttributeKeys.AudioSamplesPerSecond), Is.EqualTo(sampleRate));
        });

        using var reader = new MFDecoderInfo(new MFDecodingExtension())
            .Open(controller.OutputFile, new MediaOptions(MediaMode.AudioVideo));
        Assert.That(reader, Is.Not.Null);
        Assert.That(reader!.VideoInfo.FrameSize, Is.EqualTo(new PixelSize(128, 96)));
        Assert.That(reader.VideoInfo.FrameRate.ToDouble(), Is.EqualTo(30).Within(0.001));
        Assert.That(reader.VideoInfo.Duration.ToDouble(), Is.EqualTo(1).Within(0.05));
        Assert.That(reader.ReadVideo(0, out var image), Is.True);
        using (image)
        {
            using var bitmap = image!.Value.Convert(BitmapColorType.Bgra8888);
            // Green increases from top to bottom; blue increases left to right. Detect flips and B/R swaps.
            Assert.That(bitmap.GetRow(80)[16 * 4 + 1], Is.GreaterThan(bitmap.GetRow(16)[16 * 4 + 1] + 80));
            Assert.That(bitmap.GetRow(16)[100 * 4], Is.GreaterThan(bitmap.GetRow(16)[16 * 4] + 100));
            Assert.That(bitmap.GetRow(16)[100 * 4 + 2], Is.LessThan(40));
        }

        Assert.That(reader.ReadAudio(sampleRate / 4, 1024, out var pcm), Is.True);
        using (pcm)
        {
            var data = pcm!.GetChannelData(0).ToArray();
            Assert.That(data.Length, Is.EqualTo(1024));
            Assert.That(data.Any(s => Math.Abs(s) > 0.05f), Is.True, "AAC should contain the input signal.");
        }
        AssertAudioSignal(controller.OutputFile, MFAudioCodec.AAC, samples, sampleRate);
    }

    [TestCase(MFVideoCodec.H264, ".mp4", 15)]
    [TestCase(MFVideoCodec.H264, ".mp4", 30)]
    [TestCase(MFVideoCodec.WMV7, ".wmv", 15)]
    [TestCase(MFVideoCodec.WMV7, ".wmv", 30)]
    [TestCase(MFVideoCodec.WMV8, ".wmv", 15)]
    [TestCase(MFVideoCodec.WMV8, ".wmv", 30)]
    [TestCase(MFVideoCodec.WMV9, ".wmv", 15)]
    [TestCase(MFVideoCodec.WMV9, ".wmv", 30)]
    [TestCase(MFVideoCodec.VC1, ".wmv", 15)]
    [TestCase(MFVideoCodec.VC1, ".wmv", 30)]
    public async Task Encode_UsesRequestedKeyframeInterval(MFVideoCodec codec, string extension, int interval)
    {
        const int frameCount = 120;
        var controller = CreateController(extension);
        controller.VideoSettings.Codec.Value = codec;
        controller.VideoSettings.KeyframeRate.Value = interval;
        // A static scene avoids additional scene-change keyframes.
        using var frames = new SolidFrameProvider(new PixelSize(64, 48), frameCount);
        using var samples = new TrackedSamples(0, 0);
        await controller.Encode(frames, samples, CancellationToken.None);

        MFThread.Dispatcher.Invoke(() =>
        {
            using var source = MediaFactory.MFCreateSourceReaderFromURL(controller.OutputFile, null);
            int stream = (int)SourceReaderIndex.FirstVideoStream;
            using var type = source.GetCurrentMediaType(stream);
            Assert.That(type.GetGUID(MediaTypeAttributeKeys.Subtype), Is.EqualTo(MFEncodingFormat.VideoSubtype(codec)));
            var keyframes = new List<int>();
            int count = 0;
            while (true)
            {
                using var sample = source.ReadSample(stream, SourceReaderControlFlag.None, out _, out var flags, out _);
                if (sample != null)
                {
                    if (MediaFactory.MFGetAttributeUInt32(sample, SampleAttributeKeys.CleanPoint) != 0)
                        keyframes.Add((int)Math.Round(sample.SampleTime * 30.0 / TimeSpan.TicksPerSecond));
                    count++;
                }
                if (flags.HasFlag(SourceReaderFlag.EndOfStream))
                    break;
            }
            // Windows Media can omit unchanged frames; their timestamps still identify keyframe spacing.
            if (codec == MFVideoCodec.H264)
                Assert.That(count, Is.EqualTo(frameCount));
            Assert.That(keyframes, Is.EqualTo(Enumerable.Range(0, frameCount / interval).Select(i => i * interval)));
        });
    }

    [Test]
    public async Task Encode_VideoOnly_UsesFractionalFrameRateAndDoesNotReadAudio()
    {
        var controller = CreateController();
        controller.VideoSettings.FrameRate.Value = new Rational(30000, 1001);
        using var frames = new GradientFrameProvider(30, controller.VideoSettings.FrameRate.Value, 64, 48);
        using var samples = new TrackedSamples(0, 0);
        await controller.Encode(frames, samples, CancellationToken.None);
        using var reader = new MFDecoderInfo(new MFDecodingExtension())
            .Open(controller.OutputFile, new MediaOptions(MediaMode.Video));
        Assert.That(reader, Is.Not.Null);
        Assert.That(reader!.HasAudio, Is.False);
        Assert.That(reader.VideoInfo.FrameRate.ToDouble(), Is.EqualTo(30000d / 1001).Within(0.001));
        Assert.That(reader.VideoInfo.Duration.ToDouble(), Is.EqualTo(1.001).Within(0.01));
        Assert.That(samples.RequestedSamples, Is.Zero);
    }

    [TestCase(".m4a", MFAudioCodec.Auto, MFAudioCodec.AAC, MFAudioBitDepth.Bit16)]
    [TestCase(".m4a", MFAudioCodec.MP3, MFAudioCodec.MP3, MFAudioBitDepth.Bit16)]
    [TestCase(".m4a", MFAudioCodec.ALAC, MFAudioCodec.ALAC, MFAudioBitDepth.Bit16)]
    [TestCase(".m4a", MFAudioCodec.ALAC, MFAudioCodec.ALAC, MFAudioBitDepth.Bit24)]
    [TestCase(".mp3", MFAudioCodec.Auto, MFAudioCodec.MP3, MFAudioBitDepth.Bit16)]
    [TestCase(".wma", MFAudioCodec.Auto, MFAudioCodec.WMA, MFAudioBitDepth.Bit16)]
    [TestCase(".wma", MFAudioCodec.WMAPro, MFAudioCodec.WMAPro, MFAudioBitDepth.Bit16)]
    [TestCase(".flac", MFAudioCodec.Auto, MFAudioCodec.FLAC, MFAudioBitDepth.Bit16)]
    [TestCase(".flac", MFAudioCodec.Auto, MFAudioCodec.FLAC, MFAudioBitDepth.Bit24)]
    [TestCase(".wav", MFAudioCodec.Auto, MFAudioCodec.PCM, MFAudioBitDepth.Bit8)]
    [TestCase(".wav", MFAudioCodec.Auto, MFAudioCodec.PCM, MFAudioBitDepth.Bit16)]
    [TestCase(".wav", MFAudioCodec.PCM, MFAudioCodec.PCM, MFAudioBitDepth.Bit24)]
    [TestCase(".wav", MFAudioCodec.PCM, MFAudioCodec.PCM, MFAudioBitDepth.Bit32)]
    [TestCase(".wav", MFAudioCodec.Float, MFAudioCodec.Float, MFAudioBitDepth.Bit16)]
    [TestCase(".aac", MFAudioCodec.Auto, MFAudioCodec.AAC, MFAudioBitDepth.Bit16)]
    [TestCase(".adts", MFAudioCodec.Auto, MFAudioCodec.AAC, MFAudioBitDepth.Bit16)]
    public async Task Encode_AudioContainers_SkipVideoAndRoundTripSignal(
        string extension, MFAudioCodec codec, MFAudioCodec expected, MFAudioBitDepth bitDepth)
    {
        var controller = CreateController(extension);
        controller.AudioSettings.Codec.Value = codec;
        controller.AudioSettings.BitDepth.Value = bitDepth;
        // These settings and the non-empty frame provider must be ignored for audio-only output.
        controller.VideoSettings.DestinationSize.Value = default;
        using var frames = new FailingFrames();
        // Keep codec-frame allowances small relative to the input, with a partial final block.
        using var samples = new TrackedSamples(5 * 44100 + 137, 44100);
        await controller.Encode(frames, samples, CancellationToken.None);
        Assert.That(samples.RequestedSamples, Is.EqualTo(samples.SampleCount));
        Assert.That(samples.WasDisposed || frames.WasDisposed, Is.False);

        MFThread.Dispatcher.Invoke(() =>
        {
            using var source = MediaFactory.MFCreateSourceReaderFromURL(controller.OutputFile, null);
            using var audio = source.GetNativeMediaType((int)SourceReaderIndex.FirstAudioStream, 0);
            Assert.That(audio.GetGUID(MediaTypeAttributeKeys.Subtype), Is.EqualTo(AudioSourceSubtype(expected)));
            Assert.That(audio.GetUInt32(MediaTypeAttributeKeys.AudioSamplesPerSecond), Is.EqualTo(44100));
            Assert.That(audio.GetUInt32(MediaTypeAttributeKeys.AudioNumChannels), Is.EqualTo(2));
            if (expected is MFAudioCodec.PCM or MFAudioCodec.FLAC or MFAudioCodec.ALAC or MFAudioCodec.Float)
                Assert.That(audio.GetUInt32(MediaTypeAttributeKeys.AudioBitsPerSample),
                    Is.EqualTo(expected == MFAudioCodec.Float ? 32 : (int)bitDepth));
        });
        AssertAudioSignal(controller.OutputFile, expected, samples, controller.AudioSettings.SampleRate.Value);
    }

    [TestCase(".wmv", MFVideoCodec.Auto, MFVideoCodec.WMV9, MFAudioCodec.Auto, MFAudioCodec.WMA)]
    [TestCase(".asf", MFVideoCodec.WMV7, MFVideoCodec.WMV7, MFAudioCodec.Auto, MFAudioCodec.WMA)]
    [TestCase(".wmv", MFVideoCodec.WMV8, MFVideoCodec.WMV8, MFAudioCodec.WMAPro, MFAudioCodec.WMAPro)]
    [TestCase(".wmv", MFVideoCodec.VC1, MFVideoCodec.VC1, MFAudioCodec.Auto, MFAudioCodec.WMA)]
    [TestCase(".avi", MFVideoCodec.Auto, MFVideoCodec.RGB32, MFAudioCodec.Auto, MFAudioCodec.PCM)]
    [TestCase(".mp4", MFVideoCodec.HEVC, MFVideoCodec.HEVC, MFAudioCodec.Auto, MFAudioCodec.AAC)]
    [TestCase(".mp4", MFVideoCodec.Auto, MFVideoCodec.H264, MFAudioCodec.MP3, MFAudioCodec.MP3)]
    [TestCase(".mov", MFVideoCodec.Auto, MFVideoCodec.H264, MFAudioCodec.ALAC, MFAudioCodec.ALAC)]
    [TestCase(".m4v", MFVideoCodec.Auto, MFVideoCodec.H264, MFAudioCodec.Auto, MFAudioCodec.AAC)]
    [TestCase(".3gp", MFVideoCodec.Auto, MFVideoCodec.H264, MFAudioCodec.Auto, MFAudioCodec.AAC)]
    [TestCase(".3gpp", MFVideoCodec.Auto, MFVideoCodec.H264, MFAudioCodec.Auto, MFAudioCodec.AAC)]
    [TestCase(".3g2", MFVideoCodec.Auto, MFVideoCodec.H264, MFAudioCodec.Auto, MFAudioCodec.AAC)]
    [TestCase(".3gp2", MFVideoCodec.Auto, MFVideoCodec.H264, MFAudioCodec.Auto, MFAudioCodec.AAC)]
    public async Task Encode_VideoContainers_RoundTripBothStreams(
        string extension, MFVideoCodec codec, MFVideoCodec expectedVideo, MFAudioCodec audioCodec, MFAudioCodec expectedAudio)
    {
        if (expectedVideo == MFVideoCodec.HEVC
            && !MFThread.Dispatcher.Invoke(() => MFEncodingController.IsVideoEncoderAvailable(expectedVideo, true)))
            Assert.Ignore("No HEVC encoder is installed on this system.");

        var controller = CreateController(extension);
        controller.VideoSettings.Codec.Value = codec;
        controller.VideoSettings.UseHardwareAcceleration.Value = expectedVideo == MFVideoCodec.HEVC;
        controller.VideoSettings.DestinationSize.Value = new PixelSize(320, 240);
        controller.AudioSettings.Codec.Value = audioCodec;
        using var frames = new GradientFrameProvider(30, new Rational(30, 1), 64, 48);
        using var samples = new TrackedSamples(44100 + 137, 44100);
        await controller.Encode(frames, samples, CancellationToken.None);
        MFThread.Dispatcher.Invoke(() =>
        {
            using var source = MediaFactory.MFCreateSourceReaderFromURL(controller.OutputFile, null);
            using var video = source.GetNativeMediaType((int)SourceReaderIndex.FirstVideoStream, 0);
            using var audio = source.GetNativeMediaType((int)SourceReaderIndex.FirstAudioStream, 0);
            Assert.That(video.GetGUID(MediaTypeAttributeKeys.Subtype), Is.EqualTo(MFEncodingFormat.VideoSubtype(expectedVideo)));
            Assert.That(audio.GetGUID(MediaTypeAttributeKeys.Subtype), Is.EqualTo(AudioSourceSubtype(expectedAudio)));
        });
        MFThread.Dispatcher.Invoke(() =>
        {
            using var attributes = MediaFactory.MFCreateAttributes(1);
            attributes.Set(SourceReaderAttributeKeys.EnableVideoProcessing, true).CheckError();
            using var source = MediaFactory.MFCreateSourceReaderFromURL(controller.OutputFile, attributes);
            int stream = (int)SourceReaderIndex.FirstVideoStream;
            source.SetStreamSelection((int)SourceReaderIndex.AllStreams, false);
            source.SetStreamSelection(stream, true);
            using var type = MediaFactory.MFCreateMediaType();
            type.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video).CheckError();
            type.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.Rgb32).CheckError();
            source.SetCurrentMediaType(stream, type);
            using var current = source.GetCurrentMediaType(stream);
            Assert.That(current.GetUInt64(MediaTypeAttributeKeys.FrameSize), Is.EqualTo(((ulong)320 << 32) | 240));
            using var sample = source.ReadSample(stream, SourceReaderControlFlag.None, out _, out _, out _);
            Assert.That(sample, Is.Not.Null);
            using var buffer = sample.ConvertToContiguousBuffer();
            var data = CopyBuffer(buffer);
            int stride = unchecked((int)current.GetUInt32(MediaTypeAttributeKeys.DefaultStride));
            int top = stride < 0 ? 239 * -stride : 0;
            Assert.That(data[top + 200 * stride + 40 * 4 + 1], Is.GreaterThan(data[top + 40 * stride + 40 * 4 + 1] + 80));
            Assert.That(data[top + 40 * stride + 250 * 4], Is.GreaterThan(data[top + 40 * stride + 40 * 4] + 100));
        });
        AssertAudioSignal(controller.OutputFile, expectedAudio, samples, controller.AudioSettings.SampleRate.Value);
    }

    // The MP4 source reports ALAC using its sample-entry FourCC rather than the WAVEFORMAT GUID.
    private static Guid AudioSourceSubtype(MFAudioCodec codec) => codec == MFAudioCodec.ALAC
        ? new Guid("616c6163-767a-494d-b478-f29d25dc9037") : MFEncodingFormat.AudioSubtype(codec);

    private static void AssertAudioSignal(string file, MFAudioCodec codec, ISampleProvider input, int sampleRate)
    {
        MFThread.Dispatcher.Invoke(() =>
        {
            using var source = MediaFactory.MFCreateSourceReaderFromURL(file, null);
            int stream = (int)SourceReaderIndex.FirstAudioStream;
            source.SetStreamSelection((int)SourceReaderIndex.AllStreams, false);
            source.SetStreamSelection(stream, true);
            using var type = MFEncodingController.CreatePcm(AudioFormatGuids.Float, sampleRate, 2, 32);
            source.SetCurrentMediaType(stream, type);
            bool signal = false;
            long sampleCount = 0;
            while (true)
            {
                using var sample = source.ReadSample(stream, SourceReaderControlFlag.None, out _, out var flags, out _);
                if (sample != null)
                {
                    using var buffer = sample.ConvertToContiguousBuffer();
                    byte[] bytes = CopyBuffer(buffer);
                    var floats = MemoryMarshal.Cast<byte, float>(bytes);
                    sampleCount += floats.Length / 2;
                    foreach (float value in floats)
                        signal |= Math.Abs(value) > 0.05f;
                }
                if (flags.HasFlag(SourceReaderFlag.EndOfStream))
                    break;
            }
            Assert.That(signal, Is.True, $"{codec} must contain the input signal.");
            long expectedSamples = (long)Math.Round((double)input.SampleCount * sampleRate / input.SampleRate);
            // Bound native frame padding instead of allowing a fixed fraction of the clip.
            int padding = codec switch
            {
                MFAudioCodec.AAC => 2 * 1024,
                MFAudioCodec.MP3 => 2 * 1152,
                MFAudioCodec.WMA => 2 * 2048,
                MFAudioCodec.ALAC => 4096,
                _ => 0,
            };
            // Budget three MP3 frames for priming/trimming and 5 ms for AVI timestamp rounding.
            // Other paths permit only a single sample of resampling rounding.
            int trimming = codec == MFAudioCodec.MP3 ? 3 * 1152
                : Path.GetExtension(file) == ".avi" ? sampleRate / 200 : input.SampleRate == sampleRate ? 0 : 1;
            Assert.That(sampleCount, Is.InRange(expectedSamples - trimming, expectedSamples + padding),
                $"{codec} must preserve the input duration within codec framing and container rounding limits.");
        });
    }

    private static byte[] CopyBuffer(IMFMediaBuffer buffer)
    {
        buffer.Lock(out nint data, out _, out int length);
        try
        {
            byte[] result = new byte[length];
            Marshal.Copy(data, result, 0, length);
            return result;
        }
        finally
        {
            buffer.Unlock();
        }
    }

    [Test]
    public async Task Encode_DisabledVideo_DoesNotRenderFrames()
    {
        var controller = CreateController();
        controller.VideoSettings.Codec.Value = MFVideoCodec.None;
        using var frames = new FailingFrames();
        using var samples = new TrackedSamples(44100, 44100);
        await controller.Encode(frames, samples, CancellationToken.None);
        using var reader = new MFDecoderInfo(new MFDecodingExtension())
            .Open(controller.OutputFile, new MediaOptions(MediaMode.Audio));
        Assert.That(reader, Is.Not.Null);
        Assert.That(reader!.HasVideo, Is.False);
    }

    [Test]
    public async Task Encode_DisabledAudio_DoesNotRequestSamples()
    {
        var controller = CreateController();
        controller.AudioSettings.Codec.Value = MFAudioCodec.None;
        using var frames = new GradientFrameProvider(1, new Rational(30, 1), 64, 48);
        using var samples = new TrackedSamples(44100, 0);
        await controller.Encode(frames, samples, CancellationToken.None);
        Assert.That(samples.RequestedSamples, Is.Zero);
        using var reader = new MFDecoderInfo(new MFDecodingExtension())
            .Open(controller.OutputFile, new MediaOptions(MediaMode.Video));
        Assert.That(reader, Is.Not.Null);
        Assert.That(reader!.HasAudio, Is.False);
    }

    [TestCase(".mp4", MFVideoCodec.WMV9, MFAudioCodec.Auto)]
    [TestCase(".wmv", MFVideoCodec.H264, MFAudioCodec.Auto)]
    [TestCase(".wav", MFVideoCodec.Auto, MFAudioCodec.AAC)]
    [TestCase(".mp3", MFVideoCodec.Auto, MFAudioCodec.None)]
    [TestCase(".avi", MFVideoCodec.None, MFAudioCodec.Auto)]
    public void Encode_InvalidCodecCombination_DoesNotTruncateExistingFile(
        string extension, MFVideoCodec videoCodec, MFAudioCodec audioCodec)
    {
        var controller = CreateController(extension);
        controller.VideoSettings.Codec.Value = videoCodec;
        controller.AudioSettings.Codec.Value = audioCodec;
        File.WriteAllText(controller.OutputFile, "keep existing file");
        using var frames = new FailingFrames();
        using var samples = new TrackedSamples(44100, 44100);
        Assert.That(async () => await controller.Encode(frames, samples, CancellationToken.None), Throws.ArgumentException);
        Assert.That(File.ReadAllText(controller.OutputFile), Is.EqualTo("keep existing file"));
    }

    [Test]
    public void Encode_UnsupportedAudioRate_DoesNotTruncateExistingFile()
    {
        var controller = CreateController(".mp3");
        controller.AudioSettings.SampleRate.Value = 12345;
        File.WriteAllText(controller.OutputFile, "keep existing file");
        using var frames = new FailingFrames();
        using var samples = new TrackedSamples(44100, 44100);
        Assert.That(async () => await controller.Encode(frames, samples, CancellationToken.None),
            Throws.TypeOf<NotSupportedException>().With.Message.Contains("12345"));
        Assert.That(File.ReadAllText(controller.OutputFile), Is.EqualTo("keep existing file"));
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public void Encode_CancellationThrowsAndReleasesOutput(bool beforeStart, bool existingFile)
    {
        var controller = CreateController();
        if (existingFile) File.WriteAllText(controller.OutputFile, "keep existing file");
        using var cancellation = new CancellationTokenSource();
        using var frames = new CancelAfterFirstFrameProvider(cancellation, 30, new Rational(30, 1), 64, 48);
        using var samples = new TrackedSamples(44100, 44100);
        if (beforeStart) cancellation.Cancel();

        Assert.That(async () => await controller.Encode(frames, samples, cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());
        Assert.That(samples.WasDisposed, Is.False);
        Assert.That(Directory.GetDirectories(_directory), Is.Empty);
        if (existingFile)
            Assert.That(File.ReadAllText(controller.OutputFile), Is.EqualTo("keep existing file"));
        else if (beforeStart)
            Assert.That(File.Exists(controller.OutputFile), Is.False);
        else
            using (File.Open(controller.OutputFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
    }

    [Test]
    public void Encode_UnsupportedVideoSize_DoesNotTruncateExistingFile()
    {
        var controller = CreateController();
        controller.VideoSettings.DestinationSize.Value = new PixelSize(2, 2);
        File.WriteAllText(controller.OutputFile, "keep existing file");
        using var frames = new FailingFrames();
        using var samples = new TrackedSamples(0, 0);
        Assert.That(async () => await controller.Encode(frames, samples, CancellationToken.None),
            Throws.TypeOf<SharpGen.Runtime.SharpGenException>());
        Assert.That(File.ReadAllText(controller.OutputFile), Is.EqualTo("keep existing file"));
        Assert.That(Directory.GetDirectories(_directory), Is.Empty);
    }

    [Test]
    public void Encode_InvalidSettingsDoNotTruncateExistingFile()
    {
        var controller = CreateController();
        controller.VideoSettings.DestinationSize.Value = new PixelSize(63, 48);
        File.WriteAllText(controller.OutputFile, "keep existing file");
        using var frames = new GradientFrameProvider(1, new Rational(30, 1), 64, 48);
        using var samples = new TrackedSamples(0, 0);
        Assert.That(async () => await controller.Encode(frames, samples, CancellationToken.None),
            Throws.ArgumentException.With.Message.Contains("even"));
        Assert.That(File.ReadAllText(controller.OutputFile), Is.EqualTo("keep existing file"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Encode_AsynchronousProviderFailurePropagatesAndReleasesOutput(bool existingFile)
    {
        var controller = CreateController();
        if (existingFile) File.WriteAllText(controller.OutputFile, "keep existing file");
        using var frames = new FailingFrames();
        using var samples = new TrackedSamples(44100, 44100);
        Assert.That(async () => await controller.Encode(frames, samples, CancellationToken.None),
            Throws.InvalidOperationException.With.Message.EqualTo("Render failed."));
        Assert.That(frames.WasDisposed, Is.False);
        Assert.That(samples.WasDisposed, Is.False);
        Assert.That(Directory.GetDirectories(_directory), Is.Empty);
        if (existingFile)
            Assert.That(File.ReadAllText(controller.OutputFile), Is.EqualTo("keep existing file"));
        using (File.Open(controller.OutputFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
    }

    private MFEncodingController CreateController(string extension = ".mp4")
    {
        var controller = new MFEncodingController(Path.Combine(_directory, "output" + extension));
        controller.VideoSettings.UseHardwareAcceleration.Value = false;
        controller.VideoSettings.SourceSize.Value = new PixelSize(64, 48);
        controller.VideoSettings.DestinationSize.Value = new PixelSize(64, 48);
        return controller;
    }

    private sealed class TrackedSamples(long count, int rate) : ISampleProvider
    {
        private readonly SineSampleProvider _inner = new(count, rate);
        public long SampleCount => count;
        public long SampleRate => rate;
        public long RequestedSamples { get; private set; }
        public bool WasDisposed { get; private set; }

        public async ValueTask<AudioBuffer?> ReadNextSamples(int length)
        {
            await Task.Yield();
            Assert.That(length, Is.InRange(1, count - RequestedSamples));
            RequestedSamples += length;
            return await _inner.ReadNextSamples(length);
        }

        public void Dispose() => WasDisposed = true;
    }

    private sealed class FailingFrames : IFrameProvider
    {
        public long FrameCount => 30;
        public Rational FrameRate => new(30, 1);
        public bool WasDisposed { get; private set; }

        public async ValueTask<Bitmap?> ReadNextFrame()
        {
            await Task.Yield();
            throw new InvalidOperationException("Render failed.");
        }

        public void Dispose() => WasDisposed = true;
    }
}

[TestFixture]
public class MFEncodingTimestampTests
{
    [Test]
    public void FractionalRateDoesNotAccumulateRoundingError()
    {
        Assert.That(MFEncodingController.GetTimestamp(30000, 30000, 1001), Is.EqualTo(1001L * TimeSpan.TicksPerSecond));
        Assert.That(MFEncodingController.GetTimestamp(1, 30000, 1001), Is.EqualTo(333666));
        Assert.That(MFEncodingController.GetTimestamp(2, 30000, 1001), Is.EqualTo(667333));
    }

    [Test]
    public void LongTimelineDoesNotOverflowIntermediateProduct()
    {
        long position = 44100L * 86400 * 365;
        Assert.That(MFEncodingController.GetTimestamp(position, 44100), Is.EqualTo(TimeSpan.TicksPerDay * 365));
    }
}
