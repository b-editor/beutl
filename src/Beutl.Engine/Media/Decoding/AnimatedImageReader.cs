using System.Diagnostics.CodeAnalysis;
using Beutl.Media.Music;
using Beutl.Media.Pixel;
using SkiaSharp;

namespace Beutl.Media.Decoding;

public class AnimatedImageReader : MediaReader
{
    private SKCodec? _codec;
    private readonly SKCodecFrameInfo[] _frameInfo;
    private readonly int _frameCount;
    private readonly int _repetitionCount;

    public AnimatedImageReader(string file)
    {
        _codec = SKCodec.Create(file, out var codecResult);
        if (codecResult != SKCodecResult.Success)
            throw new Exception($"Failed to open codec: {codecResult}");

        var duration = new Rational(_codec.FrameInfo.Select(i => i.Duration).Sum(), 1000);
        VideoInfo = new VideoStreamInfo(
            _codec.EncodedFormat.ToString(),
            duration,
            new PixelSize(_codec.Info.Width, _codec.Info.Height),
            new Rational(_codec.FrameCount * duration.Denominator, duration.Numerator)
        );
        _frameCount = _codec.FrameCount;
        _frameInfo = _codec.FrameInfo;
        _repetitionCount = _codec.RepetitionCount;
    }

    public override VideoStreamInfo VideoInfo { get; }

    public override AudioStreamInfo AudioInfo => throw new NotSupportedException();

    public override bool HasVideo => true;

    public override bool HasAudio => false;

    public override bool ReadVideo(int frame, [NotNullWhen(true)] out IBitmap? image)
    {
        image = null;
        if (_codec == null)
            return false;

        // frameを秒数に変換
        long ms = frame * VideoInfo.FrameRate.Denominator * 1000 / VideoInfo.FrameRate.Numerator;
        // var seconds = frame / VideoInfo.FrameRate;

        int detectedFrame = -1;
        if (_repetitionCount == 0)
        {
            detectedFrame = 0;
        }
        else
        {
            int totalDuration = 0;
            for (int rp = 0; rp < _repetitionCount || _repetitionCount == -1; rp++)
            {
                for (int i = 0; i < _frameCount; i++)
                {
                    var info = _frameInfo[i];
                    if (ms <= totalDuration)
                    {
                        detectedFrame = i;
                        goto BreakNestedLoop;
                    }

                    totalDuration += info.Duration;
                }
            }

            BreakNestedLoop: ;
        }

        if (detectedFrame == -1)
            return false;

        var bitmap = new Bitmap<Bgra8888>(VideoInfo.FrameSize.Width, VideoInfo.FrameSize.Height);
        var imageInfo = new SKImageInfo(bitmap.Width, bitmap.Height, SKColorType.Bgra8888);
        var result = _codec.GetPixels(imageInfo, bitmap.Data, new SKCodecOptions(detectedFrame));
        if (result != SKCodecResult.Success)
            return false;

        image = bitmap;
        return true;
    }

    public override bool ReadAudio(int start, int length, [NotNullWhen(true)] out IPcm? sound)
    {
        sound = null;
        return false;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _codec?.Dispose();
        _codec = null;
    }
}
