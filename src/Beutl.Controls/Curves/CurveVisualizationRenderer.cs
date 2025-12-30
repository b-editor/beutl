#nullable enable

using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using Avalonia.Platform;

namespace Beutl.Controls.Curves;

[Flags]
public enum HistogramCategory
{
    None = 0,
    Rgb = 1,
    Hue = 2,
    Luminance = 4,
    Saturation = 8,
    All = Rgb | Hue | Luminance | Saturation
}

public sealed class CurveVisualizationRenderer
{
    private readonly int[] _rHist = new int[256];
    private readonly int[] _gHist = new int[256];
    private readonly int[] _bHist = new int[256];
    private readonly int[] _combinedHist = new int[256];
    private readonly int[] _hueHist = new int[360];
    private readonly int[] _lumaHist = new int[256];
    private readonly int[] _satHist = new int[256];
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

        s_masterBackBrush = new LinearGradientBrush
        {
            GradientStops =
            [
                new GradientStop(Color.FromArgb(40, 128, 128, 128), 0),
                new GradientStop(Color.FromArgb(20, 128, 128, 128), 1)
            ],
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        }.ToImmutable();

        s_redBackBrush = new LinearGradientBrush
        {
            GradientStops =
            [
                new GradientStop(Color.FromArgb(40, 139, 0, 0), 0),
                new GradientStop(Color.FromArgb(20, 139, 0, 0), 1)
            ],
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        }.ToImmutable();

        s_greenBackBrush = new LinearGradientBrush
        {
            GradientStops =
            [
                new GradientStop(Color.FromArgb(40, 0, 100, 0), 0),
                new GradientStop(Color.FromArgb(20, 0, 100, 0), 1)
            ],
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        }.ToImmutable();

        s_blueBackBrush = new LinearGradientBrush
        {
            GradientStops =
            [
                new GradientStop(Color.FromArgb(40, 0, 0, 139), 0),
                new GradientStop(Color.FromArgb(20, 0, 0, 139), 1)
            ],
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        }.ToImmutable();
    }

    public event EventHandler? Updated;

    public void UpdateHistogram(WriteableBitmap? sourceBitmap, HistogramCategory categories = HistogramCategory.All)
    {
        if (categories == HistogramCategory.None)
            return;

        bool needRgb = categories.HasFlag(HistogramCategory.Rgb);
        bool needHue = categories.HasFlag(HistogramCategory.Hue);
        bool needLuma = categories.HasFlag(HistogramCategory.Luminance);
        bool needSat = categories.HasFlag(HistogramCategory.Saturation);
        bool needHsl = needHue || needLuma || needSat;

        if (needRgb)
        {
            Array.Clear(_rHist);
            Array.Clear(_gHist);
            Array.Clear(_bHist);
            Array.Clear(_combinedHist);
            _histMax = 0;
        }

        if (needHue)
        {
            Array.Clear(_hueHist);
            _hueHistMax = 0;
        }

        if (needLuma)
        {
            Array.Clear(_lumaHist);
            _lumaHistMax = 0;
        }

        if (needSat)
        {
            Array.Clear(_satHist);
            _satHistMax = 0;
        }

        if (sourceBitmap is not { } bitmap)
            return;

        using ILockedFramebuffer frame = bitmap.Lock();
        unsafe
        {
            var span = new ReadOnlySpan<byte>((void*)frame.Address, frame.RowBytes * frame.Size.Height);
            int stepX = Math.Max(1, frame.Size.Width / 256);
            int stepY = Math.Max(1, frame.Size.Height / 256);

            for (int y = 0; y < frame.Size.Height; y += stepY)
            {
                int row = y * frame.RowBytes;
                for (int x = 0; x < frame.Size.Width; x += stepX)
                {
                    int idx = row + (x * 4);
                    byte b = span[idx];
                    byte g = span[idx + 1];
                    byte r = span[idx + 2];

                    if (needRgb)
                    {
                        _bHist[b]++;
                        _gHist[g]++;
                        _rHist[r]++;

                        int luminance = (int)Math.Round(r * 0.2126 + g * 0.7152 + b * 0.0722, MidpointRounding.AwayFromZero);
                        _combinedHist[luminance]++;
                    }

                    if (needHsl)
                    {
                        RgbToHsl(r, g, b, out int hue, out int sat, out int luma);

                        if (needHue && sat > 10)
                        {
                            _hueHist[hue]++;
                        }

                        if (needLuma)
                        {
                            _lumaHist[luma]++;
                        }

                        if (needSat)
                        {
                            _satHist[sat]++;
                        }
                    }
                }
            }

            if (needRgb)
            {
                _histMax = Math.Max(1,
                    Math.Max(_combinedHist.Max(), Math.Max(_rHist.Max(), Math.Max(_gHist.Max(), _bHist.Max()))));
            }

            if (needHue)
            {
                _hueHistMax = Math.Max(1, _hueHist.Max());
            }

            if (needLuma)
            {
                _lumaHistMax = Math.Max(1, _lumaHist.Max());
            }

            if (needSat)
            {
                _satHistMax = Math.Max(1, _satHist.Max());
            }
        }

        Updated?.Invoke(this, EventArgs.Empty);
    }

    private static void RgbToHsl(byte r, byte g, byte b, out int hue, out int saturation, out int luminance)
    {
        float rf = r / 255f;
        float gf = g / 255f;
        float bf = b / 255f;

        float max = Math.Max(rf, Math.Max(gf, bf));
        float min = Math.Min(rf, Math.Min(gf, bf));
        float delta = max - min;

        // Luminance
        float l = (max + min) / 2f;
        luminance = (int)Math.Round(l * 255);

        // Saturation
        float s = 0;
        if (delta > 0.0001f)
        {
            s = delta / (1f - Math.Abs(2f * l - 1f));
        }
        saturation = (int)Math.Clamp(Math.Round(s * 255), 0, 255);

        // Hue
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
                DrawHueGradient(context, bounds);
                break;

            case CurveVisualization.LuminanceVsSaturation:
                DrawLuminanceGradient(context, bounds);
                break;

            case CurveVisualization.SaturationVsSaturation:
                DrawSaturationGradient(context, bounds);
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

    private void DrawHueGradient(DrawingContext context, Rect bounds)
    {
        context.DrawRectangle(s_hueGradientBrush, null, bounds);
        context.DrawRectangle(s_blackOverlayBrush, null, bounds);

        if (_hueHistMax > 0)
        {
            double barWidth = bounds.Width / 360.0;

            for (int i = 0; i < 360; i++)
            {
                double height = bounds.Height * _hueHist[i] / _hueHistMax;
                var rect = new Rect(bounds.X + i * barWidth, bounds.Bottom - height, Math.Max(1, barWidth), height);
                context.DrawRectangle(s_whiteBarBrush, null, rect);
            }
        }
    }

    private void DrawLuminanceGradient(DrawingContext context, Rect bounds)
    {
        context.DrawRectangle(s_luminanceGradientBrush, null, bounds);
        context.DrawRectangle(s_blackOverlayBrush, null, bounds);

        if (_lumaHistMax > 0)
        {
            double barWidth = bounds.Width / 256.0;

            for (int i = 0; i < 256; i++)
            {
                double height = bounds.Height * _lumaHist[i] / _lumaHistMax;
                var rect = new Rect(bounds.X + i * barWidth, bounds.Bottom - height, Math.Max(1, barWidth), height);
                context.DrawRectangle(s_whiteBarBrush, null, rect);
            }
        }
    }

    private void DrawSaturationGradient(DrawingContext context, Rect bounds)
    {
        context.DrawRectangle(s_luminanceGradientBrush, null, bounds);
        context.DrawRectangle(s_blackOverlayBrush, null, bounds);

        if (_satHistMax > 0)
        {
            double barWidth = bounds.Width / 256.0;

            for (int i = 0; i < 256; i++)
            {
                double height = bounds.Height * _satHist[i] / _satHistMax;
                var rect = new Rect(bounds.X + i * barWidth, bounds.Bottom - height, Math.Max(1, barWidth), height);
                context.DrawRectangle(s_whiteBarBrush, null, rect);
            }
        }
    }
}
