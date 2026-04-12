using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Beutl.Editor.Components.ColorScopesTab.ViewModels;
using Beutl.Media;
using Beutl.Media.Pixel;
using BtlBitmap = Beutl.Media.Bitmap;
using PixelSize = Avalonia.PixelSize;
using SysVector = System.Numerics.Vector;
using VectorF = System.Numerics.Vector<float>;

namespace Beutl.Editor.Components.ColorScopesTab.Views.Scopes;

public class WaveformControl : HdrScopeControlBase
{
    public static readonly DirectProperty<WaveformControl, WaveformMode> ModeProperty =
        AvaloniaProperty.RegisterDirect<WaveformControl, WaveformMode>(
            nameof(Mode), o => o.Mode, (o, v) => o.Mode = v, WaveformMode.Luma);

    public static readonly DirectProperty<WaveformControl, float> ThicknessProperty =
        AvaloniaProperty.RegisterDirect<WaveformControl, float>(
            nameof(Thickness), o => o.Thickness, (o, v) => o.Thickness = v, 5f);

    public static readonly DirectProperty<WaveformControl, float> GainProperty =
        AvaloniaProperty.RegisterDirect<WaveformControl, float>(
            nameof(Gain), o => o.Gain, (o, v) => o.Gain = v, 10.0f);

    public static readonly DirectProperty<WaveformControl, bool> ShowGridProperty =
        AvaloniaProperty.RegisterDirect<WaveformControl, bool>(
            nameof(ShowGrid), o => o.ShowGrid, (o, v) => o.ShowGrid = v, true);

    private WaveformMode _mode = WaveformMode.Luma;
    private float _thickness = 5f;
    private float _gain = 10.0f;
    private bool _showGrid = true;

    // Cached gridStrength array (invalidated on height/hdrRange change)
    private float[]? _cachedGridStrength;
    private int _cachedGridHeight;
    private float _cachedGridHdrRange = float.NaN;

    private static readonly string[] s_verticalLabelsSdr = ["1.0", "0.8", "0.5", "0.3", "0"];
    private static readonly (float R, float G, float B) s_colorLuma = (0.85f, 0.92f, 1.00f);
    private static readonly (float R, float G, float B) s_colorRed = (1.00f, 0.25f, 0.25f);
    private static readonly (float R, float G, float B) s_colorGreen = (0.25f, 1.00f, 0.35f);
    private static readonly (float R, float G, float B) s_colorBlue = (0.35f, 0.60f, 1.00f);
    private static readonly (float R, float G, float B) s_colorGrid = (0.08f, 0.08f, 0.09f);

    static WaveformControl()
    {
        AffectsRender<WaveformControl>(ModeProperty, ThicknessProperty, GainProperty, ShowGridProperty);
        ModeProperty.Changed.AddClassHandler<WaveformControl>((o, _) => o.Refresh());
    }

    protected override Orientation DragAxis => Orientation.Vertical;

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

    protected override string[]? VerticalAxisLabels =>
        HdrRange is > 0.99f and < 1.01f
            ? s_verticalLabelsSdr
            : [FormatRange(HdrRange), FormatRange(HdrRange * 0.75f), FormatRange(HdrRange * 0.5f), FormatRange(HdrRange * 0.25f), "0"];

    private static string FormatRange(float v) => v >= 10 ? $"{v:F0}" : $"{v:F1}";

    protected override string[]? HorizontalAxisLabels => null;

    protected override unsafe WriteableBitmap? RenderScope(
        BtlBitmap sourceBitmap,
        int targetWidth,
        int targetHeight,
        WriteableBitmap? existingBitmap)
    {
        WriteableBitmap result =
            existingBitmap?.PixelSize.Width == targetWidth && existingBitmap.PixelSize.Height == targetHeight
                ? existingBitmap
                : new WriteableBitmap(
                    new PixelSize(targetWidth, targetHeight),
                    new Vector(96, 96),
                    PixelFormat.Bgra8888,
                    AlphaFormat.Premul);

        int sourceWidth = sourceBitmap.Width;
        int sourceHeight = sourceBitmap.Height;
        if (targetWidth == 0 || targetHeight == 0 || sourceWidth == 0 || sourceHeight == 0)
        {
            return result;
        }

        WaveformMode mode = Mode;
        float thickness = Thickness;
        float gain = Gain;
        bool showGrid = ShowGrid;
        float hdrRange = HdrRange;
        float[]? gridStrength = showGrid ? GetGridStrength(targetHeight, hdrRange) : null;
        int sampleCount = (int)Math.Clamp(sourceBitmap.Height * 0.25f, 32f, 1024f);
        float invSamplesGain = gain / Math.Max(sampleCount, 1);
        float invTargetWidth = 1f / targetWidth;
        float invHdr = 1f / MathF.Max(hdrRange, 1e-6f);

        // Pre-compute Gaussian kernel LUT (shared across all samples since thickness is constant per frame)
        int kernelRadius = Math.Max((int)MathF.Ceiling(thickness * 3f), 1);
        int kernelSize = 2 * kernelRadius + 1;
        float[] kernelArr = new float[kernelSize];
        float invKernelDenom = 1f / MathF.Max(thickness, 1e-3f);
        for (int k = 0; k < kernelSize; k++)
        {
            float dv = (k - kernelRadius) * invKernelDenom;
            float dSq = dv * dv;
            kernelArr[k] = 1f / (1f + dSq + 0.5f * dSq * dSq);
        }

        // Pre-compute srcY indices (shared across all columns)
        float invSampleCount = 1f / sampleCount;
        int[] srcYArr = new int[sampleCount];
        for (int i = 0; i < sampleCount; i++)
            srcYArr[i] = Math.Clamp((int)((i + 0.5f) * invSampleCount * sourceHeight), 0, sourceHeight - 1);

        // Pre-compute srcX and parade band per column
        int[] srcXArr = new int[targetWidth];
        int[]? paradeBandArr = mode == WaveformMode.RgbParade ? new int[targetWidth] : null;
        for (int x = 0; x < targetWidth; x++)
        {
            float x01 = (x + 0.5f) * invTargetWidth;
            if (paradeBandArr != null)
            {
                float x3 = x01 * 3f;
                int band = Math.Min(2, (int)MathF.Floor(x3));
                paradeBandArr[x] = band;
                x01 = x3 - band;
            }
            srcXArr[x] = Math.Clamp((int)(x01 * sourceWidth), 0, sourceWidth - 1);
        }

        using ILockedFramebuffer fb = result.Lock();
        BtlBitmap rgbaSrgb;
        bool requireDispose = false;
        if (sourceBitmap.ColorType == BitmapColorType.RgbaF16 && sourceBitmap.ColorSpace == BitmapColorSpace.Srgb)
        {
            rgbaSrgb = sourceBitmap;
        }
        else
        {
            rgbaSrgb = sourceBitmap.Convert(BitmapColorType.RgbaF16, BitmapAlphaType.Unpremul, BitmapColorSpace.Srgb);
            requireDispose = true;
        }

        try
        {
            byte* destPtr = (byte*)fb.Address;
            int destRowBytes = fb.RowBytes;
            // Direct data access — bypasses GetRow overhead (ThrowIfDisposed + ThrowRowOutOfRange + MemoryMarshal.Cast) per sample
            byte* srcData = (byte*)rgbaSrgb.Data;
            int srcRowBytes = rgbaSrgb.RowBytes;
            bool premul = rgbaSrgb.AlphaType == BitmapAlphaType.Premul;

            Parallel.For(0, targetWidth,
                // Per-thread local: reuse 4 float buffers across columns to avoid stackalloc / heap alloc per iteration
                () => new[]
                {
                    new float[targetHeight], new float[targetHeight],
                    new float[targetHeight], new float[targetHeight]
                },
                (x, _, bufs) =>
                {
                    float[] rBuf = bufs[0];
                    float[] gBuf = bufs[1];
                    float[] bBuf = bufs[2];
                    float[] yBuf = bufs[3];
                    Array.Clear(rBuf);
                    Array.Clear(gBuf);
                    Array.Clear(bBuf);
                    Array.Clear(yBuf);

                    int srcX = srcXArr[x];

                    // Mode-specific sampling — eliminates per-sample branching
                    if (mode == WaveformMode.Luma)
                    {
                        SampleLuma(srcData, srcRowBytes, srcX, srcYArr, sampleCount, premul,
                            yBuf, targetHeight, invHdr, kernelArr, kernelRadius);
                    }
                    else if (mode == WaveformMode.RgbOverlay)
                    {
                        SampleRgbOverlay(srcData, srcRowBytes, srcX, srcYArr, sampleCount, premul,
                            rBuf, gBuf, bBuf, targetHeight, invHdr, kernelArr, kernelRadius);
                    }
                    else
                    {
                        int band = paradeBandArr![x];
                        float[] target = band == 0 ? rBuf : band == 1 ? gBuf : bBuf;
                        SampleParade(srcData, srcRowBytes, srcX, srcYArr, sampleCount, premul, band,
                            target, targetHeight, invHdr, kernelArr, kernelRadius);
                    }

                    WriteColumn(
                        destPtr, destRowBytes, x, targetHeight,
                        rBuf, gBuf, bBuf, yBuf,
                        invSamplesGain, mode, showGrid, gridStrength);

                    return bufs;
                },
                _ => { });
        }
        finally
        {
            if (requireDispose)
                rgbaSrgb.Dispose();
        }

        return result;
    }

    private static unsafe void SampleLuma(
        byte* srcData, int srcRowBytes, int srcX, int[] srcYArr, int sampleCount, bool premul,
        float[] yBuf, int height, float invHdr, float[] kernel, int radius)
    {
        for (int i = 0; i < sampleCount; i++)
        {
            RgbaF16* pixel = (RgbaF16*)(srcData + (long)srcYArr[i] * srcRowBytes) + srcX;
            float r = (float)pixel->R;
            float g = (float)pixel->G;
            float b = (float)pixel->B;

            if (premul)
            {
                float a = (float)pixel->A;
                if (a > 0f && a < 1f)
                {
                    float invA = 1f / a;
                    r *= invA;
                    g *= invA;
                    b *= invA;
                }
            }

            float y = 0.2126f * r + 0.7152f * g + 0.0722f * b;
            AddContribution(yBuf, y, height, invHdr, kernel, radius);
        }
    }

    private static unsafe void SampleRgbOverlay(
        byte* srcData, int srcRowBytes, int srcX, int[] srcYArr, int sampleCount, bool premul,
        float[] rBuf, float[] gBuf, float[] bBuf,
        int height, float invHdr, float[] kernel, int radius)
    {
        for (int i = 0; i < sampleCount; i++)
        {
            RgbaF16* pixel = (RgbaF16*)(srcData + (long)srcYArr[i] * srcRowBytes) + srcX;
            float r = (float)pixel->R;
            float g = (float)pixel->G;
            float b = (float)pixel->B;

            if (premul)
            {
                float a = (float)pixel->A;
                if (a > 0f && a < 1f)
                {
                    float invA = 1f / a;
                    r *= invA;
                    g *= invA;
                    b *= invA;
                }
            }

            AddContributionRgb(rBuf, gBuf, bBuf, r, g, b, height, invHdr, kernel, radius);
        }
    }

    private static unsafe void SampleParade(
        byte* srcData, int srcRowBytes, int srcX, int[] srcYArr, int sampleCount, bool premul,
        int band, float[] target, int height, float invHdr, float[] kernel, int radius)
    {
        for (int i = 0; i < sampleCount; i++)
        {
            RgbaF16* pixel = (RgbaF16*)(srcData + (long)srcYArr[i] * srcRowBytes) + srcX;
            float r = (float)pixel->R;
            float g = (float)pixel->G;
            float b = (float)pixel->B;

            if (premul)
            {
                float a = (float)pixel->A;
                if (a > 0f && a < 1f)
                {
                    float invA = 1f / a;
                    r *= invA;
                    g *= invA;
                    b *= invA;
                }
            }

            float channel = band == 0 ? r : band == 1 ? g : b;
            AddContribution(target, channel, height, invHdr, kernel, radius);
        }
    }

    private static unsafe void WriteColumn(
        byte* destPtr, int destRowBytes, int x, int targetHeight,
        float[] rBuf, float[] gBuf, float[] bBuf, float[] yBuf,
        float invSamplesGain, WaveformMode mode, bool showGrid, float[]? gridStrength)
    {
        bool isLuma = mode == WaveformMode.Luma;
        bool useGrid = showGrid && gridStrength is not null;

        int simdCount = VectorF.Count;
        int vectorizedEnd = SysVector.IsHardwareAccelerated ? (targetHeight / simdCount) * simdCount : 0;

        if (vectorizedEnd > 0)
        {
            var vInvGain = new VectorF(invSamplesGain);
            var vHalf = new VectorF(0.5f);
            var vOne = VectorF.One;
            var vZero = VectorF.Zero;
            var v255 = new VectorF(255f);
            var v065 = new VectorF(0.65f);
            var v035 = new VectorF(0.35f);

            var vLumaR = new VectorF(s_colorLuma.R);
            var vLumaG = new VectorF(s_colorLuma.G);
            var vLumaB = new VectorF(s_colorLuma.B);
            var vRedR = new VectorF(s_colorRed.R);
            var vRedG = new VectorF(s_colorRed.G);
            var vRedB = new VectorF(s_colorRed.B);
            var vGreenR = new VectorF(s_colorGreen.R);
            var vGreenG = new VectorF(s_colorGreen.G);
            var vGreenB = new VectorF(s_colorGreen.B);
            var vBlueR = new VectorF(s_colorBlue.R);
            var vBlueG = new VectorF(s_colorBlue.G);
            var vBlueB = new VectorF(s_colorBlue.B);
            var vGridR = new VectorF(s_colorGrid.R);
            var vGridG = new VectorF(s_colorGrid.G);
            var vGridB = new VectorF(s_colorGrid.B);

            Span<float> rSpan = stackalloc float[simdCount];
            Span<float> gSpan = stackalloc float[simdCount];
            Span<float> bSpan = stackalloc float[simdCount];

            for (int y = 0; y < vectorizedEnd; y += simdCount)
            {
                VectorF rrIn = new VectorF(rBuf, y) * vInvGain;
                VectorF ggIn = new VectorF(gBuf, y) * vInvGain;
                VectorF bbIn = new VectorF(bBuf, y) * vInvGain;
                VectorF yyIn = new VectorF(yBuf, y) * vInvGain;

                // Fast tone-mapping: x / (1 + x * 0.5)
                VectorF rr = rrIn / (vOne + rrIn * vHalf);
                VectorF gg = ggIn / (vOne + ggIn * vHalf);
                VectorF bb = bbIn / (vOne + bbIn * vHalf);
                VectorF yy = yyIn / (vOne + yyIn * vHalf);

                VectorF colR, colG, colB;
                if (isLuma)
                {
                    colR = yy * vLumaR;
                    colG = yy * vLumaG;
                    colB = yy * vLumaB;
                }
                else
                {
                    colR = rr * vRedR + gg * vGreenR + bb * vBlueR;
                    colG = rr * vRedG + gg * vGreenG + bb * vBlueG;
                    colB = rr * vRedB + gg * vGreenB + bb * vBlueB;
                }

                VectorF gridVec = useGrid ? new VectorF(gridStrength!, y) : vZero;
                VectorF valR = SysVector.Max(vZero, colR + gridVec * vGridR);
                VectorF valG = SysVector.Max(vZero, colG + gridVec * vGridG);
                VectorF valB = SysVector.Max(vZero, colB + gridVec * vGridB);

                // Fast gamma: sqrt(v) * (0.65 + 0.35*v)
                colR = SysVector.SquareRoot(valR) * (v065 + v035 * valR);
                colG = SysVector.SquareRoot(valG) * (v065 + v035 * valG);
                colB = SysVector.SquareRoot(valB) * (v065 + v035 * valB);

                // Clamp to [0,1] and scale to 0-255
                colR = SysVector.Min(vOne, SysVector.Max(vZero, colR)) * v255;
                colG = SysVector.Min(vOne, SysVector.Max(vZero, colG)) * v255;
                colB = SysVector.Min(vOne, SysVector.Max(vZero, colB)) * v255;

                colR.CopyTo(rSpan);
                colG.CopyTo(gSpan);
                colB.CopyTo(bSpan);

                for (int k = 0; k < simdCount; k++)
                {
                    int destIndex = ((y + k) * destRowBytes) + (x * 4);
                    destPtr[destIndex + 0] = (byte)bSpan[k];
                    destPtr[destIndex + 1] = (byte)gSpan[k];
                    destPtr[destIndex + 2] = (byte)rSpan[k];
                    destPtr[destIndex + 3] = 255;
                }
            }
        }

        // Scalar tail
        for (int y = vectorizedEnd; y < targetHeight; y++)
        {
            float gridVal = useGrid ? gridStrength![y] : 0f;
            float gridR = gridVal * s_colorGrid.R;
            float gridG = gridVal * s_colorGrid.G;
            float gridB = gridVal * s_colorGrid.B;

            float rrIn = rBuf[y] * invSamplesGain;
            float ggIn = gBuf[y] * invSamplesGain;
            float bbIn = bBuf[y] * invSamplesGain;
            float yyIn = yBuf[y] * invSamplesGain;

            float rr = rrIn / (1f + rrIn * 0.5f);
            float gg = ggIn / (1f + ggIn * 0.5f);
            float bb = bbIn / (1f + bbIn * 0.5f);
            float yy = yyIn / (1f + yyIn * 0.5f);

            float colR, colG, colB;
            if (isLuma)
            {
                colR = yy * s_colorLuma.R;
                colG = yy * s_colorLuma.G;
                colB = yy * s_colorLuma.B;
            }
            else
            {
                colR = rr * s_colorRed.R + gg * s_colorGreen.R + bb * s_colorBlue.R;
                colG = rr * s_colorRed.G + gg * s_colorGreen.G + bb * s_colorBlue.G;
                colB = rr * s_colorRed.B + gg * s_colorGreen.B + bb * s_colorBlue.B;
            }

            float valR = MathF.Max(0f, colR + gridR);
            float valG = MathF.Max(0f, colG + gridG);
            float valB = MathF.Max(0f, colB + gridB);

            colR = MathF.Sqrt(valR) * (0.65f + 0.35f * valR);
            colG = MathF.Sqrt(valG) * (0.65f + 0.35f * valG);
            colB = MathF.Sqrt(valB) * (0.65f + 0.35f * valB);

            int destIndex = (y * destRowBytes) + (x * 4);
            destPtr[destIndex + 0] = (byte)(Math.Clamp(colB, 0f, 1f) * 255f);
            destPtr[destIndex + 1] = (byte)(Math.Clamp(colG, 0f, 1f) * 255f);
            destPtr[destIndex + 2] = (byte)(Math.Clamp(colR, 0f, 1f) * 255f);
            destPtr[destIndex + 3] = 255;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AddContribution(float[] buffer, float value, int height, float invHdr, float[] kernel, int radius)
    {
        float normalized = Math.Clamp(value * invHdr, 0f, 1f);
        float center = (1f - normalized) * height;
        int centerInt = (int)MathF.Round(center);
        int start = Math.Max(0, centerInt - radius);
        int end = Math.Min(height - 1, centerInt + radius);
        int kOffset = start - (centerInt - radius);
        for (int y = start, k = kOffset; y <= end; y++, k++)
        {
            buffer[y] += kernel[k];
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AddContributionRgb(
        float[] rBuf, float[] gBuf, float[] bBuf,
        float r, float g, float b,
        int height, float invHdr, float[] kernel, int radius)
    {
        float rNorm = Math.Clamp(r * invHdr, 0f, 1f);
        float gNorm = Math.Clamp(g * invHdr, 0f, 1f);
        float bNorm = Math.Clamp(b * invHdr, 0f, 1f);
        int rCenter = (int)MathF.Round((1f - rNorm) * height);
        int gCenter = (int)MathF.Round((1f - gNorm) * height);
        int bCenter = (int)MathF.Round((1f - bNorm) * height);

        int rStart = Math.Max(0, rCenter - radius);
        int rEnd = Math.Min(height - 1, rCenter + radius);
        int rKOffset = rStart - (rCenter - radius);
        for (int y = rStart, k = rKOffset; y <= rEnd; y++, k++)
            rBuf[y] += kernel[k];

        int gStart = Math.Max(0, gCenter - radius);
        int gEnd = Math.Min(height - 1, gCenter + radius);
        int gKOffset = gStart - (gCenter - radius);
        for (int y = gStart, k = gKOffset; y <= gEnd; y++, k++)
            gBuf[y] += kernel[k];

        int bStart = Math.Max(0, bCenter - radius);
        int bEnd = Math.Min(height - 1, bCenter + radius);
        int bKOffset = bStart - (bCenter - radius);
        for (int y = bStart, k = bKOffset; y <= bEnd; y++, k++)
            bBuf[y] += kernel[k];
    }

    private float[] GetGridStrength(int height, float hdrRange)
    {
        if (_cachedGridStrength != null && _cachedGridHeight == height && _cachedGridHdrRange == hdrRange)
            return _cachedGridStrength;

        _cachedGridStrength = CreateGridStrength(height, hdrRange);
        _cachedGridHeight = height;
        _cachedGridHdrRange = hdrRange;
        return _cachedGridStrength;
    }

    private static float[] CreateGridStrength(int height, float hdrRange = 1f)
    {
        var grid = new float[height];

        for (int y = 0; y < height; y++)
        {
            float v = (y + 0.5f) / height;
            float g = 0f;
            // 0%, 25%, 50%, 75%, 100% of hdrRange
            g += 0.35f * GridLine(v, 1f - 0f, 0.75f, height);
            g += 0.25f * GridLine(v, 1f - 0.25f, 0.75f, height);
            g += 0.30f * GridLine(v, 1f - 0.50f, 0.75f, height);
            g += 0.25f * GridLine(v, 1f - 0.75f, 0.75f, height);
            g += 0.35f * GridLine(v, 1f - 1f, 0.75f, height);

            // HDRモード時: SDR参照ライン（value=1.0）を表示
            if (hdrRange > 1.01f)
            {
                float sdrLine = 1f - (1f / hdrRange);
                g += 0.50f * GridLine(v, sdrLine, 0.75f, height);
            }

            grid[y] = g * 0.12f;
        }

        return grid;
    }

    private static float GridLine(float v, float t, float px, int height)
    {
        float d = MathF.Abs(v - t);
        float k = (d * height) / MathF.Max(px, 1e-3f);
        float kSq = k * k;
        // Fast Gaussian approximation: exp(-x^2) ≈ 1/(1 + x^2 + 0.5*x^4)
        return 1f / (1f + kSq + 0.5f * kSq * kSq);
    }
}
