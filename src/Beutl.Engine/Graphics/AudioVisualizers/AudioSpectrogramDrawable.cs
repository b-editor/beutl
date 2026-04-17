using System.ComponentModel.DataAnnotations;
using Beutl.Audio;
using Beutl.Audio.Graph;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Media;

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
        private const int IntensityLevels = 16;

        private SolidColorBrush.Resource?[]? _intensityBrushes;
        private Color _intensityBrushBaseColor;

        protected override (TimeSpan Start, TimeSpan Duration) ComputeSampleWindow(TimeSpan currentTime)
        {
            TimeSpan window = TimeSpan.FromSeconds(Math.Max(0.01, WindowSeconds));
            return (currentTime - window, window);
        }

        partial void PostUpdate(AudioSpectrogramDrawable obj, CompositionContext context)
        {
            EnsureIntensityBrushes(context);
        }

        partial void PostDispose(bool disposing)
        {
            if (disposing)
            {
                DisposeIntensityBrushes();
            }
            _intensityBrushes = null;
        }

        private void DisposeIntensityBrushes()
        {
            if (_intensityBrushes == null) return;
            for (int i = 0; i < _intensityBrushes.Length; i++)
            {
                _intensityBrushes[i]?.Dispose();
                _intensityBrushes[i] = null;
            }
        }

        private void EnsureIntensityBrushes(CompositionContext context)
        {
            Color baseColor = ForegroundColor;
            if (_intensityBrushes != null && _intensityBrushBaseColor == baseColor) return;

            DisposeIntensityBrushes();
            _intensityBrushes = new SolidColorBrush.Resource?[IntensityLevels];
            for (int i = 0; i < IntensityLevels; i++)
            {
                byte alpha = (byte)Math.Clamp(baseColor.A * (i + 1) / IntensityLevels, 0, 255);
                var color = Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B);
                _intensityBrushes[i] = new SolidColorBrush(color).ToResource(context) as SolidColorBrush.Resource;
            }
            _intensityBrushBaseColor = baseColor;
        }

        private SolidColorBrush.Resource? PickIntensityBrush(float normalized)
        {
            if (_intensityBrushes == null) return null;
            int idx = (int)(normalized * IntensityLevels);
            if (idx <= 0) return null;
            if (idx >= IntensityLevels) idx = IntensityLevels - 1;
            return _intensityBrushes[idx];
        }

        protected override void RenderForeground(GraphicsContext2D context, Rect bounds)
        {
            float[] samples = CachedSamples;
            if (samples.Length == 0 || CachedSampleRate <= 0) return;

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
            float floorDb = FloorDb;

            var real = new float[fftSize];
            var imag = new float[fftSize];
            var mags = new float[bins];

            int sampleCount = samples.Length;

            for (int col = 0; col < columns; col++)
            {
                float normalizedT = (col + 0.5f) / columns;
                int centerSample = (int)(normalizedT * sampleCount);

                Array.Clear(imag, 0, fftSize);
                SoundSamplingHelper.ExtractWindow(samples, centerSample, real);
                Fft.ApplyHann(real);
                Fft.Forward(real, imag);
                Fft.Magnitudes(real, imag, mags);

                float colX = bounds.X + col * colWidth;
                float drawColWidth = Math.Max(1f, colWidth + 0.5f);

                for (int bin = 0; bin < bins; bin++)
                {
                    float db = Fft.MagnitudeToDb(mags[bin] * gain, reference);
                    float normalized = (db - floorDb) / (0f - floorDb);
                    normalized = Math.Clamp(normalized, 0f, 1f);
                    SolidColorBrush.Resource? brush = PickIntensityBrush(normalized);
                    if (brush == null) continue;

                    // 高い周波数を上側に配置
                    float y = bounds.Y + height - (bin + 1) * binHeight;
                    context.DrawRectangle(
                        new Rect(colX, y, drawColWidth, Math.Max(1f, binHeight + 0.5f)),
                        brush,
                        null);
                }
            }
        }
    }
}
