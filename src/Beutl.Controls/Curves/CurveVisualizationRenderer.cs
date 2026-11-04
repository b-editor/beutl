using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Pixel;
using Beutl.Media.Source;
using Microsoft.Extensions.Logging;
using Color = Avalonia.Media.Color;
using Colors = Avalonia.Media.Colors;
using GradientStop = Avalonia.Media.GradientStop;
using LinearGradientBrush = Avalonia.Media.LinearGradientBrush;

namespace Beutl.Controls.Curves;

[Flags]
public enum HistogramCategory
{
    None = 0,
    Rgb = 1,
    Hue = 2,
    Luminance = 4,
    Saturation = 8
}

public sealed class CurveVisualizationRenderer
{
    private static readonly ILogger s_logger = Log.CreateLogger<CurveVisualizationRenderer>();

    private int[] _rHist = new int[256];
    private int[] _gHist = new int[256];
    private int[] _bHist = new int[256];
    private int[] _combinedHist = new int[256];
    private int[] _hueHist = new int[360];
    private int[] _lumaHist = new int[256];
    private int[] _satHist = new int[256];
    private (Ref<Bitmap>? Bitmap, HistogramCategory Categories)? _pending;
    private bool _isCounting;
    private int _histMax;
    private int _hueHistMax;
    private int _lumaHistMax;
    private int _satHistMax;

    private static readonly ImmutableSolidColorBrush s_blackOverlayBrush =
        new(Colors.Black, 0.55);

    private static readonly ImmutableSolidColorBrush s_whiteBarBrush =
        new(Color.FromArgb(100, 255, 255, 255));

    private static readonly ImmutableSolidColorBrush s_masterBarBrush =
        new(Color.FromArgb(140, 255, 255, 255));

    private static readonly ImmutableSolidColorBrush s_redBarBrush =
        new(Color.FromArgb(140, 255, 0, 0));

    private static readonly ImmutableSolidColorBrush s_greenBarBrush =
        new(Color.FromArgb(140, 0, 255, 0));

    private static readonly ImmutableSolidColorBrush s_blueBarBrush =
        new(Color.FromArgb(140, 30, 144, 255));

    private static readonly IBrush s_hueGradientBrush;
    private static readonly IBrush s_luminanceGradientBrush;
    private static readonly IBrush s_masterBackBrush;
    private static readonly IBrush s_redBackBrush;
    private static readonly IBrush s_greenBackBrush;
    private static readonly IBrush s_blueBackBrush;

    static CurveVisualizationRenderer()
    {
        s_hueGradientBrush = new LinearGradientBrush
        {
            GradientStops =
            [
                new GradientStop(Colors.Red, 0.0),
                new GradientStop(Colors.Yellow, 1.0 / 6),
                new GradientStop(Colors.Green, 2.0 / 6),
                new GradientStop(Colors.Cyan, 3.0 / 6),
                new GradientStop(Colors.Blue, 4.0 / 6),
                new GradientStop(Colors.Magenta, 5.0 / 6),
                new GradientStop(Colors.Red, 1.0)
            ],
            StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
        }.ToImmutable();

        s_luminanceGradientBrush = new LinearGradientBrush
        {
            GradientStops =
            [
                new GradientStop(Colors.Black, 0.0),
                new GradientStop(Colors.White, 1.0)
            ],
            StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
        }.ToImmutable();

        s_masterBackBrush = CreateVerticalFade(128, 128, 128);
        s_redBackBrush = CreateVerticalFade(139, 0, 0);
        s_greenBackBrush = CreateVerticalFade(0, 100, 0);
        s_blueBackBrush = CreateVerticalFade(0, 0, 139);
    }

    private static IBrush CreateVerticalFade(byte r, byte g, byte b)
    {
        return new LinearGradientBrush
        {
            GradientStops =
            [
                new GradientStop(Color.FromArgb(40, r, g, b), 0),
                new GradientStop(Color.FromArgb(20, r, g, b), 1)
            ],
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        }.ToImmutable();
    }

    public event EventHandler? Updated;

    /// <summary>
    /// Counts every pixel of the frame on a worker and raises <see cref="Updated"/> on this thread when done.
    /// </summary>
    /// <remarks>
    /// Values count gamma encoded, because that is what the Curves effect applies its curves to, so the bars
    /// line up with the curves drawn over them. Only one frame is counted at a time; a frame still waiting
    /// when a newer one arrives is skipped, so playback cannot queue work up.
    /// </remarks>
    public void UpdateHistogram(Ref<Bitmap>? sourceBitmapRef, HistogramCategory categories)
    {
        if (categories == HistogramCategory.None)
            return;

        _pending?.Bitmap?.Dispose();
        _pending = (sourceBitmapRef?.TryClone(), categories);
        if (!_isCounting)
            CountPendingFrames();
    }

    internal IReadOnlyList<int> GetHistogram(CurveVisualization visualization) => visualization switch
    {
        CurveVisualization.Master => _combinedHist,
        CurveVisualization.Red => _rHist,
        CurveVisualization.Green => _gHist,
        CurveVisualization.Blue => _bHist,
        CurveVisualization.HueVsHue or CurveVisualization.HueVsSaturation or CurveVisualization.HueVsLuminance => _hueHist,
        CurveVisualization.LuminanceVsSaturation => _lumaHist,
        CurveVisualization.SaturationVsSaturation => _satHist,
        _ => []
    };

    private async void CountPendingFrames()
    {
        _isCounting = true;
        try
        {
            while (_pending is var (bitmap, categories))
            {
                _pending = null;
                // Yielding even when the count is already done, so Updated never fires inside the caller's own call.
                HistogramCounts counts = await Task.Run(() => Count(bitmap, categories)).ConfigureAwait(
                    ConfigureAwaitOptions.ContinueOnCapturedContext | ConfigureAwaitOptions.ForceYielding);
                // The await resumed on the thread that owns the histograms.
                Apply(counts);
                Updated?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex)
        {
            s_logger.LogWarning(ex, "Counting the curve histogram failed.");
        }
        finally
        {
            _isCounting = false;
        }
    }

    private static HistogramCounts Count(Ref<Bitmap>? source, HistogramCategory categories)
    {
        var total = new HistogramCounts(categories);
        if (source is null)
            return total;

        using (source)
        {
            Bitmap bitmap = source.Value;
            bool isGamma = bitmap.ColorType == BitmapColorType.Bgra8888 && bitmap.ColorSpace == BitmapColorSpace.Srgb;
            Bitmap rgbaGamma = isGamma ? bitmap : bitmap.Convert(BitmapColorType.Bgra8888, colorSpace: BitmapColorSpace.Srgb);
            try
            {
                Parallel.For(
                    0,
                    rgbaGamma.Height,
                    () => new HistogramCounts(categories),
                    (y, _, counts) =>
                    {
                        Span<Bgra8888> row = rgbaGamma.GetRow<Bgra8888>(y);
                        for (int x = 0; x < rgbaGamma.Width; x++)
                            counts.Add(row[x]);

                        return counts;
                    },
                    counts =>
                    {
                        lock (total)
                            total.Merge(counts);
                    });
            }
            finally
            {
                if (!isGamma) rgbaGamma.Dispose();
            }
        }

        return total;
    }

    private void Apply(HistogramCounts counts)
    {
        if (counts.Categories.HasFlag(HistogramCategory.Rgb))
        {
            _rHist = counts.Red;
            _gHist = counts.Green;
            _bHist = counts.Blue;
            _combinedHist = counts.Combined;
            _histMax = Math.Max(1, Math.Max(_combinedHist.Max(), Math.Max(_rHist.Max(), Math.Max(_gHist.Max(), _bHist.Max()))));
        }

        if (counts.Categories.HasFlag(HistogramCategory.Hue))
        {
            _hueHist = counts.Hue;
            _hueHistMax = Math.Max(1, _hueHist.Max());
        }

        if (counts.Categories.HasFlag(HistogramCategory.Luminance))
        {
            _lumaHist = counts.Luminance;
            _lumaHistMax = Math.Max(1, _lumaHist.Max());
        }

        if (counts.Categories.HasFlag(HistogramCategory.Saturation))
        {
            _satHist = counts.Saturation;
            _satHistMax = Math.Max(1, _satHist.Max());
        }
    }

    private static void RgbToHsl(byte r, byte g, byte b, out int hue, out int saturation, out int luminance)
    {
        float rf = r / 255f;
        float gf = g / 255f;
        float bf = b / 255f;

        float max = Math.Max(rf, Math.Max(gf, bf));
        float min = Math.Min(rf, Math.Min(gf, bf));
        float delta = max - min;

        float l = (max + min) / 2f;
        luminance = (int)Math.Round(l * 255);

        float s = 0;
        if (delta > 0.0001f)
        {
            s = delta / (1f - Math.Abs(2f * l - 1f));
        }
        saturation = (int)Math.Clamp(Math.Round(s * 255), 0, 255);

        float h = 0;
        if (delta > 0.0001f)
        {
            if (max == rf)
            {
                h = 60f * (((gf - bf) / delta) % 6f);
            }
            else if (max == gf)
            {
                h = 60f * (((bf - rf) / delta) + 2f);
            }
            else
            {
                h = 60f * (((rf - gf) / delta) + 4f);
            }
        }
        if (h < 0) h += 360f;
        hue = (int)Math.Clamp(Math.Round(h), 0, 359);
    }

    public void Draw(DrawingContext context, Rect bounds, CurveVisualization visualization)
    {
        switch (visualization)
        {
            case CurveVisualization.None:
                break;

            case CurveVisualization.Master:
                DrawHistogram(context, bounds, s_masterBackBrush, s_masterBarBrush, _combinedHist);
                break;

            case CurveVisualization.Red:
                DrawHistogram(context, bounds, s_redBackBrush, s_redBarBrush, _rHist);
                break;

            case CurveVisualization.Green:
                DrawHistogram(context, bounds, s_greenBackBrush, s_greenBarBrush, _gHist);
                break;

            case CurveVisualization.Blue:
                DrawHistogram(context, bounds, s_blueBackBrush, s_blueBarBrush, _bHist);
                break;

            case CurveVisualization.HueVsHue:
            case CurveVisualization.HueVsSaturation:
            case CurveVisualization.HueVsLuminance:
                DrawGradientHistogram(context, bounds, s_hueGradientBrush, _hueHist, _hueHistMax);
                break;

            case CurveVisualization.LuminanceVsSaturation:
                DrawGradientHistogram(context, bounds, s_luminanceGradientBrush, _lumaHist, _lumaHistMax);
                break;

            case CurveVisualization.SaturationVsSaturation:
                DrawGradientHistogram(context, bounds, s_luminanceGradientBrush, _satHist, _satHistMax);
                break;
        }
    }

    private void DrawHistogram(DrawingContext context, Rect bounds, IBrush backBrush, IBrush barBrush, int[] histogram)
    {
        context.DrawRectangle(backBrush, null, bounds);
        if (histogram.Length == 0 || _histMax <= 0)
            return;

        double barWidth = bounds.Width / histogram.Length;

        for (int i = 0; i < histogram.Length; i++)
        {
            double height = bounds.Height * histogram[i] / _histMax;
            var rect = new Rect(bounds.X + i * barWidth, bounds.Bottom - height, Math.Max(1, barWidth - 0.5), height);
            context.DrawRectangle(barBrush, null, rect);
        }
    }

    // The hue histogram has 360 bins, the luminance and saturation ones 256.
    private static void DrawGradientHistogram(
        DrawingContext context, Rect bounds, IBrush gradientBrush, int[] histogram, int histogramMax)
    {
        context.DrawRectangle(gradientBrush, null, bounds);
        context.DrawRectangle(s_blackOverlayBrush, null, bounds);

        if (histogramMax > 0)
        {
            double barWidth = bounds.Width / histogram.Length;

            for (int i = 0; i < histogram.Length; i++)
            {
                double height = bounds.Height * histogram[i] / histogramMax;
                var rect = new Rect(bounds.X + i * barWidth, bounds.Bottom - height, Math.Max(1, barWidth), height);
                context.DrawRectangle(s_whiteBarBrush, null, rect);
            }
        }
    }

    private sealed class HistogramCounts(HistogramCategory categories)
    {
        private readonly bool _needRgb = categories.HasFlag(HistogramCategory.Rgb);
        private readonly bool _needHue = categories.HasFlag(HistogramCategory.Hue);
        private readonly bool _needLuminance = categories.HasFlag(HistogramCategory.Luminance);
        private readonly bool _needSaturation = categories.HasFlag(HistogramCategory.Saturation);

        public HistogramCategory Categories { get; } = categories;

        public int[] Red { get; } = new int[256];

        public int[] Green { get; } = new int[256];

        public int[] Blue { get; } = new int[256];

        public int[] Combined { get; } = new int[256];

        public int[] Hue { get; } = new int[360];

        public int[] Luminance { get; } = new int[256];

        public int[] Saturation { get; } = new int[256];

        public void Add(Bgra8888 pixel)
        {
            byte r = pixel.R;
            byte g = pixel.G;
            byte b = pixel.B;

            if (_needRgb)
            {
                Blue[b]++;
                Green[g]++;
                Red[r]++;

                int luminance = (int)Math.Round(r * 0.2126 + g * 0.7152 + b * 0.0722, MidpointRounding.AwayFromZero);
                Combined[luminance]++;
            }

            if (_needHue || _needLuminance || _needSaturation)
            {
                RgbToHsl(r, g, b, out int hue, out int sat, out int luma);

                if (_needHue && sat > 10)
                {
                    Hue[hue]++;
                }

                if (_needLuminance)
                {
                    Luminance[luma]++;
                }

                if (_needSaturation)
                {
                    Saturation[sat]++;
                }
            }
        }

        public void Merge(HistogramCounts other)
        {
            Add(Red, other.Red);
            Add(Green, other.Green);
            Add(Blue, other.Blue);
            Add(Combined, other.Combined);
            Add(Hue, other.Hue);
            Add(Luminance, other.Luminance);
            Add(Saturation, other.Saturation);

            static void Add(int[] total, int[] counts)
            {
                for (int i = 0; i < total.Length; i++)
                    total[i] += counts[i];
            }
        }
    }
}
