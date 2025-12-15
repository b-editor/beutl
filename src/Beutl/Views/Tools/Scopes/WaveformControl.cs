using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Beutl.Views.Tools.Scopes;

public enum WaveformMode
{
    Luma = 0,
    RgbOverlay = 1,
    RgbParade = 2
}

public class WaveformControl : ScopeControlBase
{
    public static readonly DirectProperty<WaveformControl, WaveformMode> ModeProperty =
        AvaloniaProperty.RegisterDirect<WaveformControl, WaveformMode>(
            nameof(Mode), o => o.Mode, (o, v) => o.Mode = v, WaveformMode.Luma);

    public static readonly DirectProperty<WaveformControl, float> ThicknessProperty =
        AvaloniaProperty.RegisterDirect<WaveformControl, float>(
            nameof(Thickness), o => o.Thickness, (o, v) => o.Thickness = v, 1.25f);

    public static readonly DirectProperty<WaveformControl, float> GainProperty =
        AvaloniaProperty.RegisterDirect<WaveformControl, float>(
            nameof(Gain), o => o.Gain, (o, v) => o.Gain = v, 2.0f);

    public static readonly DirectProperty<WaveformControl, bool> ShowGridProperty =
        AvaloniaProperty.RegisterDirect<WaveformControl, bool>(
            nameof(ShowGrid), o => o.ShowGrid, (o, v) => o.ShowGrid = v, true);

    private WaveformMode _mode = WaveformMode.Luma;
    private float _thickness = 1.25f;
    private float _gain = 2.0f;
    private bool _showGrid = true;

    private static readonly string[] s_verticalLabels = ["100", "75", "50", "25", "0"];
    private static readonly (float R, float G, float B) s_colorLuma = (0.85f, 0.92f, 1.00f);
    private static readonly (float R, float G, float B) s_colorRed = (1.00f, 0.25f, 0.25f);
    private static readonly (float R, float G, float B) s_colorGreen = (0.25f, 1.00f, 0.35f);
    private static readonly (float R, float G, float B) s_colorBlue = (0.35f, 0.60f, 1.00f);
    private static readonly (float R, float G, float B) s_colorGrid = (0.08f, 0.08f, 0.09f);

    static WaveformControl()
    {
        AffectsRender<WaveformControl>(ModeProperty, ThicknessProperty, GainProperty, ShowGridProperty);
    }

    public WaveformMode Mode
    {
        get => _mode;
        set => SetAndRaise(ModeProperty, ref _mode, value);
    }

    public float Thickness
    {
        get => _thickness;
        set => SetAndRaise(ThicknessProperty, ref _thickness, value);
    }

    public float Gain
    {
        get => _gain;
        set => SetAndRaise(GainProperty, ref _gain, value);
    }

    public bool ShowGrid
    {
        get => _showGrid;
        set => SetAndRaise(ShowGridProperty, ref _showGrid, value);
    }

    protected override string[]? VerticalAxisLabels => s_verticalLabels;

    protected override string[]? HorizontalAxisLabels => null;

    protected override unsafe WriteableBitmap? RenderScope(
        byte[] sourceData,
        int sourceWidth,
        int sourceHeight,
        int sourceStride,
        int targetWidth,
        int targetHeight,
        WriteableBitmap? existingBitmap,
        CancellationToken token)
    {
        if (token.IsCancellationRequested) return null;

        WriteableBitmap result = existingBitmap?.PixelSize.Width == targetWidth && existingBitmap.PixelSize.Height == targetHeight
            ? existingBitmap
            : new WriteableBitmap(
                new PixelSize(targetWidth, targetHeight),
                new Vector(96, 96),
                PixelFormat.Bgra8888,
                AlphaFormat.Premul);

        if (targetWidth == 0 || targetHeight == 0 || sourceWidth == 0 || sourceHeight == 0)
        {
            return result;
        }

        var mode = Mode;
        float thickness = Thickness;
        float gain = Gain;
        bool showGrid = ShowGrid;
        float[]? gridStrength = showGrid ? CreateGridStrength(targetHeight) : null;
        int sampleCount = (int)Math.Clamp(targetHeight * 0.25f, 32f, 512f);
        float invSamples = 1f / Math.Max(sampleCount, 1);

        using ILockedFramebuffer fb = result.Lock();

        fixed (byte* srcPtr = sourceData)
        {
            var safeSrcPtr = (IntPtr)srcPtr;
            byte* destPtr = (byte*)fb.Address;
            int destRowBytes = fb.RowBytes;

            Parallel.For(0, targetWidth, (x, state) =>
            {
                if (token.IsCancellationRequested)
                {
                    state.Break();
                    return;
                }

                float x01 = (x + 0.5f) / targetWidth;
                float paradeBand = 0f;

                if (mode == WaveformMode.RgbParade)
                {
                    float x3 = x01 * 3f;
                    paradeBand = MathF.Min(2f, MathF.Floor(x3));
                    x01 = x3 - paradeBand;
                }

                Span<float> rBuffer = targetHeight <= 1024 ? stackalloc float[targetHeight] : new float[targetHeight];
                Span<float> gBuffer = targetHeight <= 1024 ? stackalloc float[targetHeight] : new float[targetHeight];
                Span<float> bBuffer = targetHeight <= 1024 ? stackalloc float[targetHeight] : new float[targetHeight];
                Span<float> yBuffer = targetHeight <= 1024 ? stackalloc float[targetHeight] : new float[targetHeight];

                for (int i = 0; i < sampleCount; i++)
                {
                    if (token.IsCancellationRequested)
                    {
                        state.Break();
                        return;
                    }

                    float sampleY01 = (i + 0.5f) / sampleCount;
                    int srcX = Math.Clamp((int)(x01 * sourceWidth), 0, sourceWidth - 1);
                    int srcY = Math.Clamp((int)(sampleY01 * sourceHeight), 0, sourceHeight - 1);

                    byte* sample = (byte*)safeSrcPtr + (srcY * sourceStride) + (srcX * 4);
                    float b = sample[0] / 255f;
                    float g = sample[1] / 255f;
                    float r = sample[2] / 255f;
                    float a = sample[3] / 255f;

                    if (a > 0f && a < 1f)
                    {
                        float invA = 1f / a;
                        r *= invA;
                        g *= invA;
                        b *= invA;
                    }

                    float y = (0.2126f * r) + (0.7152f * g) + (0.0722f * b);

                    if (mode == WaveformMode.Luma)
                    {
                        AddContribution(yBuffer, y, targetHeight, thickness);
                    }
                    else if (mode == WaveformMode.RgbOverlay)
                    {
                        AddContribution(rBuffer, r, targetHeight, thickness);
                        AddContribution(gBuffer, g, targetHeight, thickness);
                        AddContribution(bBuffer, b, targetHeight, thickness);
                    }
                    else
                    {
                        float channel = paradeBand < 0.5f ? r : paradeBand < 1.5f ? g : b;

                        if (paradeBand < 0.5f)
                        {
                            AddContribution(rBuffer, channel, targetHeight, thickness);
                        }
                        else if (paradeBand < 1.5f)
                        {
                            AddContribution(gBuffer, channel, targetHeight, thickness);
                        }
                        else
                        {
                            AddContribution(bBuffer, channel, targetHeight, thickness);
                        }
                    }
                }

                for (int y = 0; y < targetHeight; y++)
                {
                    if (token.IsCancellationRequested)
                    {
                        state.Break();
                        return;
                    }

                    float gridVal = showGrid && gridStrength is not null ? gridStrength[y] : 0f;
                    float gridR = gridVal * s_colorGrid.R;
                    float gridG = gridVal * s_colorGrid.G;
                    float gridB = gridVal * s_colorGrid.B;

                    float rr = Math.Min(1f, rBuffer[y] * invSamples * gain);
                    float gg = Math.Min(1f, gBuffer[y] * invSamples * gain);
                    float bb = Math.Min(1f, bBuffer[y] * invSamples * gain);
                    float yy = Math.Min(1f, yBuffer[y] * invSamples * gain);

                    float colR;
                    float colG;
                    float colB;

                    if (mode == WaveformMode.Luma)
                    {
                        colR = yy * s_colorLuma.R;
                        colG = yy * s_colorLuma.G;
                        colB = yy * s_colorLuma.B;
                    }
                    else if (mode == WaveformMode.RgbOverlay)
                    {
                        colR = rr * s_colorRed.R + gg * s_colorGreen.R + bb * s_colorBlue.R;
                        colG = rr * s_colorRed.G + gg * s_colorGreen.G + bb * s_colorBlue.G;
                        colB = rr * s_colorRed.B + gg * s_colorGreen.B + bb * s_colorBlue.B;
                    }
                    else
                    {
                        colR = rr * s_colorRed.R + gg * s_colorGreen.R + bb * s_colorBlue.R;
                        colG = rr * s_colorRed.G + gg * s_colorGreen.G + bb * s_colorBlue.G;
                        colB = rr * s_colorRed.B + gg * s_colorGreen.B + bb * s_colorBlue.B;
                    }

                    colR = MathF.Pow(MathF.Max(0f, colR + gridR), 1f / 1.3f);
                    colG = MathF.Pow(MathF.Max(0f, colG + gridG), 1f / 1.3f);
                    colB = MathF.Pow(MathF.Max(0f, colB + gridB), 1f / 1.3f);

                    int destIndex = (y * destRowBytes) + (x * 4);
                    destPtr[destIndex + 0] = (byte)(Math.Clamp(colB, 0f, 1f) * 255f);
                    destPtr[destIndex + 1] = (byte)(Math.Clamp(colG, 0f, 1f) * 255f);
                    destPtr[destIndex + 2] = (byte)(Math.Clamp(colR, 0f, 1f) * 255f);
                    destPtr[destIndex + 3] = 255;
                }
            });
        }

        return result;
    }

    private static void AddContribution(Span<float> buffer, float value, int height, float thickness)
    {
        float clamped = Math.Clamp(value, 0f, 1f);
        float yPos = 1f - clamped;
        float center = yPos * height;
        float radius = MathF.Max(thickness * 3f, 1f);
        int start = Math.Max(0, (int)MathF.Floor(center - radius));
        int end = Math.Min(height - 1, (int)MathF.Ceiling(center + radius));
        float denom = MathF.Max(thickness, 1e-3f);

        for (int y = start; y <= end; y++)
        {
            float y01 = (y + 0.5f) / height;
            float k = MathF.Abs(y01 - yPos) * height / denom;
            float contribution = MathF.Exp(-(k * k));
            buffer[y] += contribution;
        }
    }

    private static float[] CreateGridStrength(int height)
    {
        var grid = new float[height];

        for (int y = 0; y < height; y++)
        {
            float v = (y + 0.5f) / height;
            float g = 0f;
            g += 0.35f * GridLine(v, 1f - 0f, 0.75f, height);
            g += 0.25f * GridLine(v, 1f - 0.25f, 0.75f, height);
            g += 0.30f * GridLine(v, 1f - 0.50f, 0.75f, height);
            g += 0.25f * GridLine(v, 1f - 0.75f, 0.75f, height);
            g += 0.35f * GridLine(v, 1f - 1f, 0.75f, height);
            grid[y] = g * 0.12f;
        }

        return grid;
    }

    private static float GridLine(float v, float t, float px, int height)
    {
        float d = MathF.Abs(v - t);
        float k = (d * height) / MathF.Max(px, 1e-3f);
        return MathF.Exp(-(k * k));
    }
}
