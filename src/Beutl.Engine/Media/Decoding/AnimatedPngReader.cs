using System.Diagnostics.CodeAnalysis;
using Beutl.Graphics;
using Beutl.Media.Decoding.APNG;
using Beutl.Media.Decoding.APNG.Chunks;
using Beutl.Media.Music;
using Beutl.Media.Pixel;
using SkiaSharp;

namespace Beutl.Media.Decoding;

public class AnimatedPngReader : MediaReader
{
    private APNG.APNG _apng;
    private readonly int _frameCount;
    private Bitmap<Bgra8888>? _defaultImage;
    private FrameCache? _lastFrame;

    public AnimatedPngReader(string file)
    {
        _apng = new APNG.APNG(file);

        var duration = new Rational(0, 1);
        foreach (var frame in _apng.Frames)
        {
            if (frame.fcTLChunk == null)
                continue;

            long delayDen = frame.fcTLChunk.DelayDen == 0 ? 100 : frame.fcTLChunk.DelayDen;
            duration += frame.fcTLChunk.DelayNum == 0
                ? new Rational(1, 1000)
                : new Rational(frame.fcTLChunk.DelayNum, delayDen);
        }

        VideoInfo = new VideoStreamInfo(
            "APNG",
            duration,
            new PixelSize(_apng.IHDRChunk.Width, _apng.IHDRChunk.Height),
            new Rational(_apng.Frames.Length * duration.Denominator, duration.Numerator)
        );
        _frameCount = _apng.Frames.Length;
        if (_apng.IsSimplePNG)
        {
            _defaultImage = Bitmap<Bgra8888>.FromStream(_apng.DefaultImage.GetStream());
        }
    }

    public override VideoStreamInfo VideoInfo { get; }

    public override AudioStreamInfo AudioInfo => throw new NotSupportedException();

    public override bool HasVideo => true;

    public override bool HasAudio => false;

    public override bool ReadVideo(int frame, [NotNullWhen(true)] out IBitmap? image)
    {
        image = null;

        // frameを秒数に変換
        var seconds = new Rational(frame * VideoInfo.FrameRate.Denominator, VideoInfo.FrameRate.Numerator);

        int detectedFrame = -1;
        if (_defaultImage != null)
        {
            image = _defaultImage.Clone();
            return true;
        }
        else
        {
            Rational totalDuration = new Rational(0, 1);
            for (int rp = 0; rp < _apng.acTLChunk!.NumPlays || _apng.acTLChunk!.NumPlays == 0; rp++)
            {
                for (int i = 0; i < _frameCount; i++)
                {
                    var f = _apng.Frames[i];
                    if (f.fcTLChunk == null)
                        continue;

                    if (seconds <= totalDuration)
                    {
                        detectedFrame = i;
                        goto BreakNestedLoop;
                    }

                    long delayDen = f.fcTLChunk.DelayDen == 0 ? 100 : f.fcTLChunk.DelayDen;
                    totalDuration += f.fcTLChunk.DelayNum == 0
                        ? new Rational(1, 1000)
                        : new Rational(f.fcTLChunk.DelayNum, delayDen);
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
        var disposeOp = _apng.Frames[detectedFrame].fcTLChunk!.DisposeOp;
        if (disposeOp != DisposeOps.APNGDisposeOpBackground)
        {
            _lastFrame = new FrameCache(bitmap, disposeOp, detectedFrame);
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

        var stack = new Stack<(Frame, int)>();
        for (int i = index; i >= 0; i--)
        {
            var info = _apng.Frames[i];
            if (i != index && info.fcTLChunk!.DisposeOp == DisposeOps.APNGDisposeOpBackground)
                break;

            stack.Push((info, i));

            if (_lastFrame is not null &&
                _lastFrame.Index == i)
            {
                break;
            }

            if (info.fcTLChunk!.BlendOp == BlendOps.APNGBlendOpSource)
            {
                break;
            }
        }

        var frameBitmap =
            new SKBitmap(new SKImageInfo(VideoInfo.FrameSize.Width, VideoInfo.FrameSize.Height, SKColorType.Bgra8888));
        using var canvas = new SKCanvas(frameBitmap);
        using var paint = new SKPaint();
        foreach ((Frame info, int i) in stack)
        {
            var fcTL = info.fcTLChunk!;
            paint.BlendMode = fcTL.BlendOp == BlendOps.APNGBlendOpOver
                ? SKBlendMode.SrcOver
                : SKBlendMode.Src;

            if (_lastFrame is not null &&
                _lastFrame.Index == i)
            {
                canvas.DrawBitmap(
                    _lastFrame.Bitmap,
                    SKRect.Create(fcTL.XOffset, fcTL.YOffset, fcTL.Width, fcTL.Height),
                    paint);
                continue;
            }

            using var tmp = SKBitmap.Decode(info.GetStream());

            canvas.DrawBitmap(
                tmp,
                SKRect.Create(fcTL.XOffset, fcTL.YOffset, fcTL.Width, fcTL.Height),
                paint);
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
        _apng = null!;
        _defaultImage?.Dispose();
        _defaultImage = null;
    }

    private record FrameCache(SKBitmap Bitmap, DisposeOps DisposalMethod, int Index) : IDisposable
    {
        public void Dispose()
        {
            Bitmap.Dispose();
        }
    }
}
