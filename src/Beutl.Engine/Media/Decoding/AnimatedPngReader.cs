using System.Diagnostics.CodeAnalysis;
using Beutl.Graphics;
using Beutl.Media.Decoding.APNG;
using Beutl.Media.Decoding.APNG.Chunks;
using Beutl.Media.Music;
using Beutl.Media.Source;
using SkiaSharp;

namespace Beutl.Media.Decoding;

public class AnimatedPngReader : MediaReader
{
    private APNG.APNG _apng;
    private readonly int _frameCount;
    private Bitmap? _defaultImage;
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
            _defaultImage = Bitmap.FromStream(_apng.DefaultImage.GetStream());
        }
    }

    public override VideoStreamInfo VideoInfo { get; }

    public override AudioStreamInfo AudioInfo => throw new NotSupportedException();

    public override bool HasVideo => true;

    public override bool HasAudio => false;

    public override bool ReadVideo(int frame, [NotNullWhen(true)] out Ref<Bitmap>? image)
    {
        image = null;

        // frameを秒数に変換
        var seconds = new Rational(frame * VideoInfo.FrameRate.Denominator, VideoInfo.FrameRate.Numerator);

        int detectedFrame = -1;
        if (_defaultImage != null)
        {
            image = Ref<Bitmap>.Create(_defaultImage.Clone());
            return true;
        }
        else
        {
            // APNG 仕様で NumPlays == 0 は無限ループ再生。元コードは
            // `for (rp; rp < NumPlays || NumPlays == 0; rp++)` で外側を永久ループに
            // していたため、要求時間がコンテンツ全体の duration を超えるとレンダリ
            // ングスレッドがハングしていた。
            //
            // 1巡分の duration を先に計算し、無限ループ素材は seconds をその範囲に
            // wrap してフレーム検出する。これでハングを防ぎつつ、playhead が 1 巡分
            // を超えても正しいフレームが返るので「再生が止まる」回帰も起きない。
            Rational cycleDuration = new Rational(0, 1);
            for (int i = 0; i < _frameCount; i++)
            {
                var f = _apng.Frames[i];
                if (f.fcTLChunk == null) continue;

                long delayDen = f.fcTLChunk.DelayDen == 0 ? 100 : f.fcTLChunk.DelayDen;
                cycleDuration += f.fcTLChunk.DelayNum == 0
                    ? new Rational(1, 1000)
                    : new Rational(f.fcTLChunk.DelayNum, delayDen);
            }

            if (cycleDuration <= new Rational(0, 1))
            {
                // 進行する frame chunk が一つも無い。元のロジックなら無限ループだったケース。
                return false;
            }

            uint numPlays = _apng.acTLChunk!.NumPlays;
            Rational effective = seconds;

            if (numPlays != 0)
            {
                Rational fullDuration = cycleDuration * (long)numPlays;
                if (effective >= fullDuration)
                {
                    // 最終再生分の最後を超えた要求は元コードでは detectedFrame=-1 のまま。
                    return false;
                }
            }

            // wrap effective into [0, cycleDuration) for both finite and infinite loops.
            while (effective >= cycleDuration)
            {
                effective -= cycleDuration;
            }

            Rational accumulated = new Rational(0, 1);
            for (int i = 0; i < _frameCount; i++)
            {
                var f = _apng.Frames[i];
                if (f.fcTLChunk == null) continue;

                if (effective <= accumulated)
                {
                    detectedFrame = i;
                    goto BreakNestedLoop;
                }

                long delayDen = f.fcTLChunk.DelayDen == 0 ? 100 : f.fcTLChunk.DelayDen;
                accumulated += f.fcTLChunk.DelayNum == 0
                    ? new Rational(1, 1000)
                    : new Rational(f.fcTLChunk.DelayNum, delayDen);
            }

            // wrap 後でもヒットしない（端数などで最終フレーム超えと判定された）場合は
            // 1 巡の最終 fcTL フレームを返す。
            for (int i = _frameCount - 1; i >= 0; i--)
            {
                if (_apng.Frames[i].fcTLChunk != null)
                {
                    detectedFrame = i;
                    break;
                }
            }

        BreakNestedLoop:;
        }

        if (detectedFrame == -1)
            return false;

        if (_lastFrame?.Index == detectedFrame)
        {
            image = Ref<Bitmap>.Create(new Bitmap(_lastFrame.Bitmap.Copy()));
            return true;
        }

        var bitmap = RenderBitmap(detectedFrame);
        var disposeOp = _apng.Frames[detectedFrame].fcTLChunk!.DisposeOp;
        // 古いキャッシュは新しいフレームで上書きする前に必ず Dispose する。
        // 抜けていると進むたびに数 MB の SKBitmap がリークする。
        _lastFrame?.Dispose();
        if (disposeOp != DisposeOps.APNGDisposeOpBackground)
        {
            _lastFrame = new FrameCache(bitmap, disposeOp, detectedFrame);
        }
        else
        {
            _lastFrame = null;
        }

        image = Ref<Bitmap>.Create(new Bitmap(bitmap.Copy()));
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

    public override bool ReadAudio(int start, int length, [NotNullWhen(true)] out Ref<IPcm>? sound)
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
        _lastFrame?.Dispose();
        _lastFrame = null;
    }

    private record FrameCache(SKBitmap Bitmap, DisposeOps DisposalMethod, int Index) : IDisposable
    {
        public void Dispose()
        {
            Bitmap.Dispose();
        }
    }
}
