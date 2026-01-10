using System.Diagnostics.CodeAnalysis;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Music;
using Beutl.Media.Pixel;
using Beutl.Serialization;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

internal static class TestMediaHelper
{
    public static Uri CreateTestImageUri(int width, int height, Color? fillColor = null)
    {
        using var bitmap = new Bitmap<Bgra8888>(width, height);

        // Fill with a solid color
        var color = fillColor ?? Colors.White;
        var pixel = new Bgra8888(color.B, color.G, color.R, color.A);
        bitmap.Fill(pixel);

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
            @"test-video-(\d+)x(\d+)@(\d+)_(\d+)f(\d+)");

        if (!match.Success)
            throw new FormatException($"Invalid test video path: {path}");

        return (
            int.Parse(match.Groups[1].Value),
            int.Parse(match.Groups[2].Value),
            new Rational(int.Parse(match.Groups[3].Value), int.Parse(match.Groups[4].Value)),
            int.Parse(match.Groups[5].Value)
        );
    }
}

internal sealed class TestDecoderInfo : IDecoderInfo
{
    public string Name => "Test Decoder";

    public MediaReader? Open(string file, MediaOptions options)
    {
        if (!IsSupported(file))
            return null;

        var (width, height, frameRate, frameCount) = TestMediaHelper.ParseTestVideoPath(file);
        return new TestMediaReader(new PixelSize(width, height), frameRate, frameCount);
    }

    public bool IsSupported(string file)
    {
        return Path.GetExtension(file).Equals(".testvideo", StringComparison.OrdinalIgnoreCase);
    }

    public IEnumerable<string> VideoExtensions() => [".testvideo"];

    public IEnumerable<string> AudioExtensions() => [];
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

    public override bool ReadVideo(int frame, [NotNullWhen(true)] out IBitmap? image)
    {
        if (frame < 0 || frame >= _frameCount)
        {
            image = null;
            return false;
        }

        // Create a simple test bitmap
        var bitmap = new Bitmap<Bgra8888>(_frameSize.Width, _frameSize.Height);
        // Fill with a frame-dependent color for testing
        byte colorValue = (byte)((frame * 10) % 256);
        bitmap.Fill(new Bgra8888(colorValue, colorValue, colorValue, 255));
        image = bitmap;
        return true;
    }

    public override bool ReadAudio(int start, int length, [NotNullWhen(true)] out IPcm? sound)
    {
        sound = null;
        return false;
    }
}
