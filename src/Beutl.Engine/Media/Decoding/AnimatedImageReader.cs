using System.Diagnostics.CodeAnalysis;
using Beutl.Audio;
using Beutl.Graphics;
using Beutl.Media.Source;
using SkiaSharp;

namespace Beutl.Media.Decoding;

public class AnimatedImageReader : MediaReader
{
    private SKCodec? _codec;
    private readonly SKCodecFrameInfo[] _frameInfo;
    private readonly int _frameCount;
    private readonly int _repetitionCount;
    private readonly int _cycleDuration;
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
        for (int i = 0; i < _frameCount; i++)
        {
            _cycleDuration += _frameInfo[i].Duration;
        }
    }

    public override VideoStreamInfo VideoInfo { get; }

    public override AudioStreamInfo AudioInfo => throw new NotSupportedException();

    public override bool HasVideo => true;

    public override bool HasAudio => false;

    // Returns the frame shown at the given time, or -1 when there is none.
    private int FindFrameIndex(long ms)
    {
        if (_cycleDuration <= 0)
        {
            return -1;
        }

        long effective = ms;

        // Skia counts the repetitions after the first pass, so 0 plays once; a negative count loops forever.
        if (_repetitionCount >= 0)
        {
            long fullDuration = _cycleDuration * (_repetitionCount + 1L);
            if (effective >= fullDuration)
            {
                return -1;
            }
        }

        // Wrap ms into one cycle so that infinitely looping media still finds a frame.
        effective %= _cycleDuration;

        int accumulated = 0;
        for (int i = 0; i < _frameCount; i++)
        {
            if (effective <= accumulated)
            {
                return i;
            }

            accumulated += _frameInfo[i].Duration;
        }

        // 端数で最終フレームを超えた判定になった場合は最終フレームを返す。
        return _frameCount - 1;
    }

    public override bool ReadVideo(int frame, [NotNullWhen(true)] out Ref<Bitmap>? image)
    {
        image = null;
        if (_codec == null)
            return false;

        // Convert the frame index to milliseconds.
        long ms = frame * VideoInfo.FrameRate.Denominator * 1000 / VideoInfo.FrameRate.Numerator;

        int detectedFrame = FindFrameIndex(ms);
        if (detectedFrame == -1)
            return false;

        if (_lastFrame?.Index == detectedFrame)
        {
            image = Ref<Bitmap>.Create(new Bitmap(_lastFrame.Bitmap.Copy()));
            return true;
        }

        var bitmap = RenderBitmap(detectedFrame);
        var disposalMethod = _frameInfo[detectedFrame].DisposalMethod;
        // 古いキャッシュは新しいフレームで上書きする前に必ず Dispose する。
        // 抜けていると進むたびに数 MB の SKBitmap がリークする。
        _lastFrame?.Dispose();
        if (disposalMethod != SKCodecAnimationDisposalMethod.RestoreBackgroundColor)
        {
            _lastFrame = new Frame(bitmap, detectedFrame);
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

        var stack = new Stack<(SKCodecFrameInfo, int)>();
        for (int i = index; i >= 0; i--)
        {
            var info = _frameInfo[i];
            if (i != index && info.DisposalMethod == SKCodecAnimationDisposalMethod.RestoreBackgroundColor)
                break;

            stack.Push((info, i));
            if (_lastFrame?.Index == i)
                break;
        }

        var frameBitmap = new SKBitmap(_codec!.Info);
        using var canvas = new SKCanvas(frameBitmap);
        foreach ((SKCodecFrameInfo info, int i) in stack)
        {
            if (_lastFrame is not null &&
                _lastFrame.Index == i)
            {
                canvas.DrawBitmap(_lastFrame.Bitmap, 0, 0, SKSamplingOptions.Default);
                continue;
            }

            using var tmp = new SKBitmap(_codec!.Info.WithAlphaType(info.AlphaType));
            var result = _codec.GetPixels(tmp.Info, tmp.GetPixels(),
                new SKCodecOptions(i) { ZeroInitialized = SKZeroInitialized.Yes });
            if (result != SKCodecResult.Success)
                throw new Exception($"Failed to decode frame {i}: {result}");

            canvas.DrawBitmap(tmp, 0, 0, SKSamplingOptions.Default);
        }

        return frameBitmap;
    }

    public override bool ReadAudio(int start, int length, [NotNullWhen(true)] out AudioBuffer? sound)
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

    private record Frame(SKBitmap Bitmap, int Index) : IDisposable
    {
        public void Dispose()
        {
            Bitmap.Dispose();
        }
    }
}
