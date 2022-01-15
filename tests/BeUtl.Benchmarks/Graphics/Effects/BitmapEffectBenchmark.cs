using BeUtl.Graphics;
using BeUtl.Graphics.Effects;
using BeUtl.Media;
using BeUtl.Media.Pixel;
using BeUtl.Threading;

using BenchmarkDotNet.Attributes;

namespace BeUtl.Benchmarks.Graphics.Effects;

public class BitmapEffectBenchmark
{
    private readonly List<BitmapEffect> _fxs = new()
    {
        new Binarization() { Value = 127 },
        new Brightness() { Value = 127 },
        new ColorAdjust() { Red = 255, Green = 255, Blue = 255 },
        new Negaposi() { Red = 127, Green = 127, Blue = 127 },
    };
    private readonly Bitmap<Bgra8888> _source;

    public BitmapEffectBenchmark()
    {
        var dispatcher = Dispatcher.Spawn();

        _source = dispatcher.Invoke(() =>
        {
            using var canvas = new Canvas(500, 500);

            canvas.StrokeWidth = 250;
            canvas.Foreground = new LinearGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(Colors.Red, 0),
                    new GradientStop(Colors.Blue, 1),
                }
            };
            canvas.DrawCircle(new Size(500, 500));

            return canvas.GetBitmap();
        });

        dispatcher.Stop();
    }

    [Benchmark]
    public void ApplyAll()
    {
        using var bmp = (Bitmap<Bgra8888>)_source.Clone();

        Bitmap<Bgra8888> result = BitmapEffect.ApplyAll(bmp, _fxs);

        result.Dispose();
    }

    [Benchmark]
    public void ApplySequential()
    {
        var bmp = (Bitmap<Bgra8888>)_source.Clone();

        for (int i = 0; i < _fxs.Count; i++)
        {
            BitmapEffect item = _fxs[i];
            item.Apply(ref bmp);
        }

        bmp.Dispose();
    }
}
