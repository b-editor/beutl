using System.Diagnostics.CodeAnalysis;
using Beutl.Graphics;
using Beutl.Media.Music;
using SkiaSharp;

namespace Beutl.Media.Decoding;

public class AnimatedImageReader : MediaReader
{
    private SKCodec? _codec;
    private readonly SKCodecFrameInfo[] _frameInfo;
    private readonly int _frameCount;
    private readonly int _repetitionCount;
    private Frame? _lastFrame;

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

        BreakNestedLoop:;
        }

        if (detectedFrame == -1)
            return false;

        if (_lastFrame?.Index == detectedFrame)
        {
            image = _lastFrame.Bitmap.ToBitmap();
            return true;
        }

        var bitmap = RenderBitmap(detectedFrame);
        var disposalMethod = _frameInfo[detectedFrame].DisposalMethod;
        if (disposalMethod != SKCodecAnimationDisposalMethod.RestoreBackgroundColor)
        {
            _lastFrame = new Frame(bitmap, disposalMethod, detectedFrame);
        }
        else
        {
            _lastFrame?.Dispose();
            _lastFrame = null;
        }

        image = bitmap.ToBitmap();
        return true;
    }

    private SKBitmap RenderBitmap(int index)
    {
        if (index < 0 || index >= _frameCount)
            throw new ArgumentOutOfRangeException(nameof(index));

        var stack = new Stack<(SKCodecFrameInfo, int)>();
        for (int i = index; i >= 0; i--)
        {
            var info = _frameInfo[i];
            if (i != index && info.DisposalMethod == SKCodecAnimationDisposalMethod.RestoreBackgroundColor)
                break;

            if (_lastFrame is not null &&
                _lastFrame.Index == i)
            {
                stack.Push((info, i));
                break;
            }

            stack.Push((info, i));
        }

        var frameBitmap = new SKBitmap(_codec!.Info);
        using var canvas = new SKCanvas(frameBitmap);
        foreach ((SKCodecFrameInfo info, int i) in stack)
        {
            if (_lastFrame is not null &&
                _lastFrame.Index == i)
            {
                canvas.DrawBitmap(_lastFrame.Bitmap, 0, 0);
                continue;
            }

            using var tmp = new SKBitmap(_codec!.Info.WithAlphaType(info.AlphaType));
            var result = _codec.GetPixels(tmp.Info, tmp.GetPixels(),
                new SKCodecOptions(i) { ZeroInitialized = SKZeroInitialized.Yes });
            if (result != SKCodecResult.Success)
                throw new Exception($"Failed to decode frame {i}: {result}");

            canvas.DrawBitmap(tmp, 0, 0);
        }

        return frameBitmap;
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
        _lastFrame?.Dispose();
        _lastFrame = null;
    }

    private record Frame(SKBitmap Bitmap, SKCodecAnimationDisposalMethod DisposalMethod, int Index) : IDisposable
    {
        public void Dispose()
        {
            Bitmap.Dispose();
        }
    }
}
