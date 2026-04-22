using System.ComponentModel.DataAnnotations;
using Beutl.Audio;
using Beutl.Audio.Graph;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.AudioVisualizers;

[Display(Name = nameof(GraphicsStrings.AudioSpectrogram), ResourceType = typeof(GraphicsStrings))]
public sealed partial class AudioSpectrogramDrawable : AudioVisualizerDrawable
{
    public AudioSpectrogramDrawable()
    {
        ScanProperties<AudioSpectrogramDrawable>();

        // 編集頻度順に並べ替え: Source → Fill → 信号調整(Gain/FloorDb) → 時間/周波数設定 → 解像度 → サイズ
        MoveProperty(Source, 0);
        MoveProperty(Fill, 1);
        MoveProperty(Gain, 2);
        MoveProperty(FloorDb, 3);
        MoveProperty(WindowSeconds, 4);
        MoveProperty(FrequencyScale, 5);
        MoveProperty(TimeColumns, 6);
        MoveProperty(FftSize, 7);
        MoveProperty(Width, 8);
        MoveProperty(Height, 9);
    }

    [Display(Name = nameof(GraphicsStrings.AudioVisualizer_WindowSeconds), ResourceType = typeof(GraphicsStrings))]
    [Range(0.01f, 3600f)]
    public IProperty<float> WindowSeconds { get; } = Property.CreateAnimatable(4f);

    [Display(Name = nameof(GraphicsStrings.AudioVisualizer_FftSize), ResourceType = typeof(GraphicsStrings))]
    [Range(64, 16384)]
    public IProperty<int> FftSize { get; } = Property.Create(512);

    [Display(Name = nameof(GraphicsStrings.AudioVisualizer_FloorDb), ResourceType = typeof(GraphicsStrings))]
    [Range(-200f, 0f)]
    public IProperty<float> FloorDb { get; } = Property.CreateAnimatable(-80f);

    [Display(Name = nameof(GraphicsStrings.AudioVisualizer_TimeColumns), ResourceType = typeof(GraphicsStrings))]
    [Range(2, 2048)]
    public IProperty<int> TimeColumns { get; } = Property.Create(256);

    [Display(Name = nameof(GraphicsStrings.AudioVisualizer_FrequencyScale), ResourceType = typeof(GraphicsStrings))]
    public IProperty<FrequencyScale> FrequencyScale { get; } = Property.Create(AudioVisualizers.FrequencyScale.Logarithmic);

    public new partial class Resource
    {
        private float[] _fftReal = [];
        private float[] _fftImag = [];
        private float[] _fftMagnitudes = [];
        private int[] _rowBinLo = [];
        private int[] _rowBinHi = [];
        private SKPaint? _paint;

        protected override (TimeSpan Start, TimeSpan Duration) ComputeSampleWindow(TimeSpan currentTime)
        {
            TimeSpan window = TimeSpan.FromSeconds(Math.Max(0.01, WindowSeconds));
            return (currentTime - window, window);
        }

        partial void PostDispose(bool disposing)
        {
            if (disposing)
            {
                _paint?.Dispose();
            }
            _paint = null;
        }

        protected override void RenderForeground(ImmediateCanvas canvas, Rect bounds)
        {
            if (CachedSampleLength == 0 || CachedSampleRate <= 0 || Fill is null) return;

            int fftSize = Fft.ClampToPowerOfTwo(FftSize);
            if (fftSize < 2) return;

            int columns = Math.Max(2, TimeColumns);
            int bins = fftSize / 2;
            float width = (float)bounds.Width;
            float height = (float)bounds.Height;
            float colWidth = width / columns;
            float reference = fftSize * 0.5f;
            float gain = Math.Max(0f, Gain);
            // FloorDb は Range(-200, 0) だが、0 に設定されると (0 - floorDb) が 0 除算になるため -0.001 以下にクランプする
            float floorDb = MathF.Min(FloorDb, -0.001f);
            FrequencyScale freqScale = FrequencyScale;

            if (_fftReal.Length < fftSize) _fftReal = new float[fftSize];
            if (_fftImag.Length < fftSize) _fftImag = new float[fftSize];
            if (_fftMagnitudes.Length < bins) _fftMagnitudes = new float[bins];
            Span<float> real = _fftReal.AsSpan(0, fftSize);
            Span<float> imag = _fftImag.AsSpan(0, fftSize);
            Span<float> mags = _fftMagnitudes.AsSpan(0, bins);

            ReadOnlySpan<float> samples = CachedSampleSpan;
            int sampleCount = samples.Length;

            // 高強度 (normalized=1) 相当の Fill を 1 度構築し、各セルは Alpha のみ書き換える。
            _paint ??= new SKPaint();
            new BrushConstructor(bounds, Fill, BlendMode.SrcOver).ConfigurePaint(_paint);
            _paint.Style = SKPaintStyle.Fill;
            SKColor baseColor = _paint.Color;
            byte baseAlpha = baseColor.Alpha;

            // 周波数軸スケール用に「bin の取得関数」を決定する。
            // y=0 が最高周波数、y=height が最低周波数として配置。
            float fMax = CachedSampleRate * 0.5f;
            float fMin = MathF.Max(20f, fMax / bins);
            double melMin = freqScale == FrequencyScale.Mel ? 2595.0 * Math.Log10(1 + fMin / 700.0) : 0;
            double melMax = freqScale == FrequencyScale.Mel ? 2595.0 * Math.Log10(1 + fMax / 700.0) : 0;

            int pixelRows = Math.Max(1, (int)MathF.Ceiling(height));
            float rowHeight = height / pixelRows;

            // 各行がカバーする FFT bin 範囲 [lo, hi) を事前計算。
            // pixelRows < bins の場合でも bin の取りこぼしが発生しないよう bin 範囲全体を描画時に max 集約する。
            if (_rowBinLo.Length < pixelRows) _rowBinLo = new int[pixelRows];
            if (_rowBinHi.Length < pixelRows) _rowBinHi = new int[pixelRows];
            for (int row = 0; row < pixelRows; row++)
            {
                // row=0 が最高周波数。下端は t_low (低周波)、上端は t_high (高周波)。
                double tLow = 1.0 - (row + 1) / (double)pixelRows;
                double tHigh = 1.0 - row / (double)pixelRows;
                if (tLow < 0) tLow = 0;
                if (tHigh > 1) tHigh = 1;
                double f1 = FreqForT(tLow, freqScale, fMin, fMax, melMin, melMax);
                double f2 = FreqForT(tHigh, freqScale, fMin, fMax, melMin, melMax);
                int bLo = (int)Math.Floor(f1 / fMax * bins);
                int bHi = (int)Math.Ceiling(f2 / fMax * bins);
                if (bLo < 0) bLo = 0;
                if (bHi > bins) bHi = bins;
                if (bHi <= bLo) bHi = Math.Min(bLo + 1, bins);
                _rowBinLo[row] = bLo;
                _rowBinHi[row] = bHi;
            }

            for (int col = 0; col < columns; col++)
            {
                float normalizedT = (col + 0.5f) / columns;
                int centerSample = (int)(normalizedT * sampleCount);

                imag.Clear();
                SoundSamplingHelper.ExtractWindow(samples, centerSample, real);
                Fft.ApplyHann(real);
                Fft.Forward(real, imag);
                Fft.Magnitudes(real, imag, mags);

                float colX = (float)bounds.X + col * colWidth;
                float drawColWidth = MathF.Max(1f, colWidth + 0.5f);

                for (int row = 0; row < pixelRows; row++)
                {
                    int bLo = _rowBinLo[row];
                    int bHi = _rowBinHi[row];

                    // bin 範囲内の最大マグニチュードで代表させる（ピーク可視化優先）
                    float peak = 0f;
                    for (int b = bLo; b < bHi; b++)
                    {
                        float m = mags[b];
                        if (m > peak) peak = m;
                    }

                    float db = Fft.MagnitudeToDb(peak * gain, reference);
                    float normalized = (db - floorDb) / (0f - floorDb);
                    normalized = Math.Clamp(normalized, 0f, 1f);
                    if (normalized <= 0f) continue;

                    byte alpha = (byte)(baseAlpha * normalized);
                    if (alpha == 0) continue;
                    _paint.Color = baseColor.WithAlpha(alpha);

                    float y = (float)bounds.Y + row * rowHeight;
                    canvas.Canvas.DrawRect(
                        new SKRect(colX, y, colX + drawColWidth, y + MathF.Max(1f, rowHeight + 0.5f)),
                        _paint);
                }
            }
        }

        private static double FreqForT(double t, FrequencyScale scale, float fMin, float fMax, double melMin, double melMax)
            => scale switch
            {
                FrequencyScale.Logarithmic => fMin * Math.Pow(fMax / fMin, t),
                FrequencyScale.Mel => 700.0 * (Math.Pow(10, (melMin + (melMax - melMin) * t) / 2595.0) - 1),
                _ => t * fMax,
            };
    }
}
