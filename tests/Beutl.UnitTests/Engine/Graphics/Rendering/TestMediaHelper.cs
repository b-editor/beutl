using System.Diagnostics.CodeAnalysis;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;
using Beutl.Media.Pixel;
using Beutl.Media.Source;
using Beutl.Serialization;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

internal static class TestMediaHelper
{
    public static Uri CreateTestImageUri(int width, int height, Color? fillColor = null)
    {
        using var bitmap = new Bitmap(width, height);

        // Fill with a solid color
        var color = fillColor ?? Colors.White;
        var pixel = new Bgra8888(color.B, color.G, color.R, color.A);
        bitmap.GetPixelSpan<Bgra8888>().Fill(pixel);

        using var stream = new MemoryStream();
        bitmap.Save(stream, EncodedImageFormat.Png);
        byte[] data = stream.ToArray();

        return UriHelper.CreateBase64DataUri("image/png", data);
    }

    public static void RegisterTestDecoder()
    {
        if (!_registered)
        {
            DecoderRegistry.Register(new TestDecoderInfo());
            _registered = true;
        }
    }

    private static bool _registered;

    public static string CreateTestVideoFile(int width, int height, Rational frameRate, int frameCount)
    {
        // Encode parameters in the file name so the test decoder can read them
        var fileName = $"test-video-{width}x{height}@{frameRate.Numerator}_{frameRate.Denominator}f{frameCount}.testvideo";
        var filePath = Path.Combine(Path.GetTempPath(), fileName);

        // Create an empty file if it doesn't exist
        if (!File.Exists(filePath))
        {
            File.WriteAllBytes(filePath, []);
        }

        return filePath;
    }

    public static (int Width, int Height, Rational FrameRate, int FrameCount) ParseTestVideoPath(string path)
    {
        var fileName = Path.GetFileNameWithoutExtension(path);
        // Format: test-video-100x100@30_1f300
        var match = System.Text.RegularExpressions.Regex.Match(
            fileName,
            @"test-video-(\d+)x(\d+)@(\d+)_(\d+)f(\d+)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (!match.Success)
            throw new FormatException($"Invalid test video path: {path}");

        return (
            int.Parse(match.Groups[1].Value),
            int.Parse(match.Groups[2].Value),
            new Rational(int.Parse(match.Groups[3].Value), int.Parse(match.Groups[4].Value)),
            int.Parse(match.Groups[5].Value)
        );
    }

    // The file NAME encodes the audio params for TestAudioReader to read back; duration is in milliseconds to keep
    // the name integer-only. The audio path routes to a separate TestAudioReader, so .testvideo is untouched.
    public static string CreateTestAudioFile(int sampleRate = 44100, int channels = 2, double durationSeconds = 2.0)
    {
        int ms = (int)Math.Round(durationSeconds * 1000);
        var fileName = $"test-audio-{sampleRate}_{channels}_{ms}.testaudio";
        var filePath = Path.Combine(Path.GetTempPath(), fileName);
        if (!File.Exists(filePath))
        {
            File.WriteAllBytes(filePath, []);
        }

        return filePath;
    }

    public static (int SampleRate, int Channels, int DurationMs) ParseTestAudioPath(string path)
    {
        var fileName = Path.GetFileNameWithoutExtension(path);
        // Format: test-audio-44100_2_2000
        var match = System.Text.RegularExpressions.Regex.Match(
            fileName,
            @"test-audio-(\d+)_(\d+)_(\d+)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!match.Success)
            throw new FormatException($"Invalid test audio path: {path}");

        return (int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value), int.Parse(match.Groups[3].Value));
    }
}

internal sealed class TestDecoderInfo : IDecoderInfo
{
    public string Name => "Test Decoder";

    public MediaReader? Open(string file, MediaOptions options)
    {
        if (Path.GetExtension(file).Equals(".testaudio", StringComparison.OrdinalIgnoreCase))
        {
            var (rate, channels, ms) = TestMediaHelper.ParseTestAudioPath(file);
            return new TestAudioReader(rate, channels, TimeSpan.FromMilliseconds(ms));
        }

        if (!IsSupported(file))
            return null;

        var (width, height, frameRate, frameCount) = TestMediaHelper.ParseTestVideoPath(file);
        return new TestMediaReader(new PixelSize(width, height), frameRate, frameCount);
    }

    public bool IsSupported(string file)
    {
        var ext = Path.GetExtension(file);
        return ext.Equals(".testvideo", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".testaudio", StringComparison.OrdinalIgnoreCase);
    }

    public IEnumerable<string> VideoExtensions() => [".testvideo"];

    public IEnumerable<string> AudioExtensions() => [".testaudio"];
}

internal sealed class TestMediaReader : MediaReader
{
    private readonly PixelSize _frameSize;
    private readonly Rational _frameRate;
    private readonly int _frameCount;
    private readonly VideoStreamInfo _videoInfo;
    private readonly AudioStreamInfo _audioInfo;

    public TestMediaReader(PixelSize frameSize, Rational frameRate, int frameCount)
    {
        _frameSize = frameSize;
        _frameRate = frameRate;
        _frameCount = frameCount;
        _videoInfo = new VideoStreamInfo(
            "test",
            frameCount,
            frameSize,
            frameRate);
        _audioInfo = new AudioStreamInfo(
            "test",
            Rational.Zero,
            44100,
            2);
    }

    public override VideoStreamInfo VideoInfo => _videoInfo;

    public override AudioStreamInfo AudioInfo => _audioInfo;

    public override bool HasVideo => true;

    public override bool HasAudio => false;

    protected override bool ReadVideoCore(int frame, [NotNullWhen(true)] out Ref<Bitmap>? image)
    {
        if (frame < 0 || frame >= _frameCount)
        {
            image = null;
            return false;
        }

        // Create a simple test bitmap
        var bitmap = new Bitmap(_frameSize.Width, _frameSize.Height);
        // Fill with a frame-dependent color for testing
        byte colorValue = (byte)((frame * 10) % 256);
        bitmap.GetPixelSpan<Bgra8888>().Fill(new Bgra8888(colorValue, colorValue, colorValue, 255));
        image = Ref<Bitmap>.Create(bitmap);
        return true;
    }

    protected override bool ReadAudioCore(int start, int length, [NotNullWhen(true)] out Ref<IPcm>? sound)
    {
        sound = null;
        return false;
    }
}

// Emits a synthetic 440 Hz stereo sine so a SourceSound-backed visualizer composes non-empty samples. Separate
// from the video TestMediaReader so the .testvideo path is untouched.
internal sealed class TestAudioReader : MediaReader
{
    private readonly VideoStreamInfo _videoInfo;
    private readonly AudioStreamInfo _audioInfo;

    public TestAudioReader(int sampleRate, int channels, TimeSpan duration)
    {
        _videoInfo = new VideoStreamInfo("test", 0, default, new Rational(1, 1));
        _audioInfo = new AudioStreamInfo(
            "test",
            new Rational(duration.Ticks, TimeSpan.TicksPerSecond),
            sampleRate,
            channels);
    }

    public override VideoStreamInfo VideoInfo => _videoInfo;

    public override AudioStreamInfo AudioInfo => _audioInfo;

    public override bool HasVideo => false;

    public override bool HasAudio => true;

    protected override bool ReadVideoCore(int frame, [NotNullWhen(true)] out Ref<Bitmap>? image)
    {
        image = null;
        return false;
    }

    protected override bool ReadAudioCore(int start, int length, [NotNullWhen(true)] out Ref<IPcm>? sound)
    {
        if (length <= 0)
        {
            sound = null;
            return false;
        }

        int rate = _audioInfo.SampleRate;
        var pcm = new Pcm<Stereo32BitFloat>(rate, length);
        Span<Stereo32BitFloat> data = pcm.DataSpan;
        const float freq = 440f;
        for (int i = 0; i < length; i++)
        {
            // start may be negative (window centred before t=0); emit a continuous tone regardless.
            long n = (long)start + i;
            float v = 0.5f * MathF.Sin(2f * MathF.PI * freq * n / rate);
            data[i] = new Stereo32BitFloat(v, v);
        }

        sound = Ref<IPcm>.Create(pcm);
        return true;
    }
}
