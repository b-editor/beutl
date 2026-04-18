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

    public new partial class Resource
    {
        private float[] _fftReal = [];
        private float[] _fftImag = [];
        private float[] _fftMagnitudes = [];
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
            float binHeight = height / bins;
            float reference = fftSize * 0.5f;
            float gain = Math.Max(0f, Gain);
            // FloorDb は Range(-200, 0) だが、0 に設定されると (0 - floorDb) が 0 除算になるため -0.001 以下にクランプする
            float floorDb = MathF.Min(FloorDb, -0.001f);

            if (_fftReal.Length < fftSize) _fftReal = new float[fftSize];
            if (_fftImag.Length < fftSize) _fftImag = new float[fftSize];
            if (_fftMagnitudes.Length < bins) _fftMagnitudes = new float[bins];
            Span<float> real = _fftReal.AsSpan(0, fftSize);
            Span<float> imag = _fftImag.AsSpan(0, fftSize);
            Span<float> mags = _fftMagnitudes.AsSpan(0, bins);

            ReadOnlySpan<float> samples = CachedSampleSpan;
            int sampleCount = samples.Length;

            // Fill の最高強度 (normalized=1) に対応するペイントを bounds 全体で 1 回構築。
            // 各セルでは opacity のみ変化させるので、SKPaint の Color の A チャンネルを毎回書き換える。
            _paint ??= new SKPaint();
            new BrushConstructor(bounds, Fill, BlendMode.SrcOver).ConfigurePaint(_paint);
            _paint.Style = SKPaintStyle.Fill;
            SKColor baseColor = _paint.Color;
            byte baseAlpha = baseColor.Alpha;

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
                float drawColWidth = Math.Max(1f, colWidth + 0.5f);

                for (int bin = 0; bin < bins; bin++)
                {
                    float db = Fft.MagnitudeToDb(mags[bin] * gain, reference);
                    float normalized = (db - floorDb) / (0f - floorDb);
                    normalized = Math.Clamp(normalized, 0f, 1f);
                    if (normalized <= 0f) continue;

                    byte alpha = (byte)(baseAlpha * normalized);
                    if (alpha == 0) continue;
                    _paint.Color = baseColor.WithAlpha(alpha);

                    // 高い周波数を上側に配置
                    float y = (float)bounds.Y + height - (bin + 1) * binHeight;
                    canvas.Canvas.DrawRect(
                        new SKRect(colX, y, colX + drawColWidth, y + Math.Max(1f, binHeight + 0.5f)),
                        _paint);
                }
            }
        }
    }
}
