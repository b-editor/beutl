using System.ComponentModel.DataAnnotations;
using Beutl.Audio.Graph;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.AudioVisualizers;

[Display(Name = nameof(GraphicsStrings.AudioSpectrum), ResourceType = typeof(GraphicsStrings))]
public sealed partial class AudioSpectrumDrawable : AudioVisualizerDrawable
{
    public AudioSpectrumDrawable()
    {
        ScanProperties<AudioSpectrumDrawable>();
        Shape.Value = new BarSpectrumShape();

        // 編集頻度順に並べ替え: Source → 見た目(Shape/Fill) → 信号調整(Gain/FloorDb) → 応答(Smoothing) → 周波数設定(Scale/BarCount) → 解像度(FftSize) → サイズ
        MoveProperty(Source, 0);
        MoveProperty(Shape, 1);
        MoveProperty(Fill, 2);
        MoveProperty(Gain, 3);
        MoveProperty(FloorDb, 4);
        MoveProperty(Smoothing, 5);
        MoveProperty(FrequencyScale, 6);
        MoveProperty(BarCount, 7);
        MoveProperty(FftSize, 8);
        MoveProperty(Width, 9);
        MoveProperty(Height, 10);
    }

    [Display(Name = nameof(GraphicsStrings.AudioVisualizer_Shape), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<SpectrumShape?> Shape { get; } = Property.CreateExpression<SpectrumShape?>();

    [Display(Name = nameof(GraphicsStrings.AudioVisualizer_BarCount), ResourceType = typeof(GraphicsStrings))]
    [Range(1, 10000)]
    public IExpressionProperty<int> BarCount { get; } = Property.CreateExpression(128);

    [Display(Name = nameof(GraphicsStrings.AudioVisualizer_FftSize), ResourceType = typeof(GraphicsStrings))]
    [Range(64, 16384)]
    public IExpressionProperty<int> FftSize { get; } = Property.CreateExpression(1024);

    [Display(Name = nameof(GraphicsStrings.AudioVisualizer_FrequencyScale), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<FrequencyScale> FrequencyScale { get; } = Property.CreateExpression(AudioVisualizers.FrequencyScale.Logarithmic);

    [Display(Name = nameof(GraphicsStrings.AudioVisualizer_FloorDb), ResourceType = typeof(GraphicsStrings))]
    [Range(-200f, 0f)]
    public IExpressionProperty<float> FloorDb { get; } = Property.CreateAnimatable(-80f);

    [Display(Name = nameof(GraphicsStrings.AudioVisualizer_Smoothing), ResourceType = typeof(GraphicsStrings))]
    [Range(0f, 99f)]
    public IExpressionProperty<float> Smoothing { get; } = Property.CreateAnimatable(85f);

    public new partial class Resource
    {
        // A hold made again looks back no further than this; a smoothing that would hold a peak longer falls faster.
        private static readonly MediaTime s_maxHoldHistory = MediaTime.FromSeconds(30);

        // How much of its height a bar may differ by between a hold made again and one played on from further back.
        private const double HoldHistoryTolerance = 0.01;

        private float[] _fftReal = [];
        private float[] _fftImag = [];
        private float[] _fftMagnitudes = [];
        // The hold at the last frame it reached on the grid of the scene's frame rate.
        private float[] _smoothedMagnitudes = [];
        private float[] _shownMagnitudes = [];
        private float[] _normalizedBars = [];
        private float[] _historyWindow = [];
        private int _shownBarCount;
        private int _sampleWindowFftSize;
        private long? _heldFrame;
        // The hold has taken in the window of every frame after this one, up to the frame it reached.
        private long _heldFrom;
        private HoldSettings _heldSettings;

        // What the held magnitudes were made with; any change makes them again. The decay is the share of the hold a
        // frame keeps: the smoothing, or, for a peak that would outlast the history, a rate the gain and the floor set.
        private readonly record struct HoldSettings(
            int FftSize, int BarCount, FrequencyScale Scale, float Decay, int SampleRate, Rational FrameRate, int SourceVersion);

        /// <summary>Gets the bars the last reconcile made, from 0 at the floor to 1 at full scale.</summary>
        internal ReadOnlySpan<float> Bars => _normalizedBars.AsSpan(0, _shownBarCount);

        // The base composes the sample window before this class reconciles FftSize.
        partial void PreReconcile(AudioSpectrumDrawable obj, CompositionContext context)
        {
            _sampleWindowFftSize = context.Get(obj.FftSize);
        }

        // The bars hold a peak and release it one step a frame, on the grid of the scene's frame rate in this
        // drawable's own time. Playing on continues the hold from the last frame; a seek, a still, a jump back or a
        // reversed clip makes it again from the frames before, so the bars at a time are the same however the frame is
        // reached (#4207).
        partial void PostReconcile(AudioSpectrumDrawable obj, CompositionContext context)
        {
            UpdateBars(context.Time, context.FrameRate);
        }

        protected override (MediaTime Start, MediaTime Duration) ComputeSampleWindow(MediaTime currentTime)
        {
            int effectiveFftSize = Fft.ClampToPowerOfTwo(_sampleWindowFftSize);
            MediaTime duration = MediaTime.FromSeconds((double)effectiveFftSize / ComposerSampleRate);
            return (currentTime - duration, duration);
        }

        protected override void RenderForeground(ImmediateCanvas canvas, Rect bounds)
        {
            if (_shownBarCount == 0 || Fill is null) return;

            SpectrumShape.Resource? shapeResource = Shape;
            if (shapeResource is null) return;

            shapeResource.Render(new SpectrumRenderContext(canvas, bounds, _normalizedBars.AsSpan(0, _shownBarCount), Fill));
        }

        private void UpdateBars(MediaTime time, Rational frameRate)
        {
            int fftSize = Fft.ClampToPowerOfTwo(FftSize);
            if (CachedSampleLength == 0 || CachedSampleRate <= 0 || fftSize < 2)
            {
                _shownBarCount = 0;
                _heldFrame = null;
                return;
            }

            if (frameRate.Numerator <= 0 || frameRate.Denominator <= 0)
                frameRate = CompositionConditions.DefaultFrameRate;

            int barCount = Math.Min(Math.Max(1, BarCount), fftSize / 2);
            float smoothing = Math.Clamp(Smoothing / 100f, 0f, 0.99f);
            // A hold made again takes in the windows of the frames after this one.
            (int history, float decay) = HoldHistory(smoothing, frameRate);
            var settings = new HoldSettings(
                fftSize, barCount, FrequencyScale, decay, CachedSampleRate, frameRate, Source?.Version ?? 0);
            if (_smoothedMagnitudes.Length < barCount) _smoothedMagnitudes = new float[barCount];
            if (_shownMagnitudes.Length < barCount) _shownMagnitudes = new float[barCount];
            if (_normalizedBars.Length < barCount) _normalizedBars = new float[barCount];
            Span<float> held = _smoothedMagnitudes.AsSpan(0, barCount);

            // The last frame of the grid at or before this time; a time between frames adds its own window on top.
            long frame = time.ToFrame(frameRate, MidpointRounding.ToNegativeInfinity);
            bool onFrame = MediaTime.FromFrames(frame, frameRate) == time;
            long from = frame - history - 1;
            // Playing on continues the hold while it reaches back as far as a hold made again would. A lower floor or a
            // higher gain moves that point back, and the hold is made again from there.
            if (_heldFrame is { } last && _heldSettings == settings && last <= frame && last > from && _heldFrom <= from)
            {
                AdvanceHold(held, last, frame, onFrame, frameRate, fftSize, decay);
            }
            else
            {
                held.Clear();
                AdvanceHold(held, from, frame, onFrame, frameRate, fftSize, decay);
                _heldFrom = from;
            }

            _heldFrame = frame;
            _heldSettings = settings;

            Span<float> shown = held;
            if (!onFrame)
            {
                shown = _shownMagnitudes.AsSpan(0, barCount);
                held.CopyTo(shown);
                Hold(shown, CachedSampleSpan, fftSize, decay);
            }

            bool changed = Normalize(shown, fftSize) | (_shownBarCount != barCount);
            _shownBarCount = barCount;
            // The bars follow the frame rate and the frames before, which no change of a property reports.
            if (changed)
                Version++;
        }

        // Takes the hold from frame `from` to frame `to` of the grid. Each frame's window is composed on its own and ends
        // at that frame, as drawing the frame composes it: an effect that keeps state between samples, such as a delay,
        // starts again on a window that does not follow the last one, so the samples are the same either way. The current
        // window, when the time is on a frame, is the one the base composed already.
        private void AdvanceHold(
            Span<float> held, long from, long to, bool onFrame, Rational frameRate, int fftSize, float smoothing)
        {
            long composedTo = onFrame ? to - 1 : to;
            for (long f = from + 1; f <= composedTo; f++)
            {
                (MediaTime start, MediaTime duration) = ComputeSampleWindow(MediaTime.FromFrames(f, frameRate));
                int length = ComposeMono(new TimeRange(start, duration), ref _historyWindow, out _);
                Hold(held, _historyWindow.AsSpan(0, length), fftSize, smoothing);
            }

            if (onFrame && to > from)
                Hold(held, CachedSampleSpan, fftSize, smoothing);
        }

        // One step of the hold: a rise follows the window at once, and a fall keeps the smoothing's share of the hold.
        private void Hold(Span<float> held, ReadOnlySpan<float> window, int fftSize, float smoothing)
        {
            int bins = fftSize / 2;
            Span<float> mags = ComputeMagnitudes(window, fftSize, bins);
            FrequencyScale freqScale = FrequencyScale;
            FrequencyAxis axis = FrequencyAxis.Create(freqScale, CachedSampleRate, bins);
            for (int i = 0; i < held.Length; i++)
            {
                ResolveBarBins(i, held.Length, bins, freqScale, in axis, out int binLow, out int binHigh);

                // バンド内は RMS で集計するとピーク値より滑らかに変化する
                float rawMag = BandRms(mags, binLow, binHigh);

                // 高速アタック + 緩やかリリース (ピークメーター方式)
                float prev = held[i];
                held[i] = rawMag > prev
                    ? rawMag
                    : prev * smoothing + rawMag * (1f - smoothing);
            }
        }

        // A full-scale peak falls by the smoothing each frame, and the hold adds each window to what is left of it. The
        // history is how many frames it takes until what is left is small enough beside the floor that adding it lifts a
        // bar by under HoldHistoryTolerance of its height, so the frames before them barely show. A peak that would take
        // longer than s_maxHoldHistory falls faster instead, so that playing on forgets it when a hold made again does.
        private (int Frames, float Decay) HoldHistory(float smoothing, Rational frameRate)
        {
            float gain = Math.Max(0f, Gain);
            if (smoothing <= 0f || gain <= 0f) return (0, smoothing);

            float floorDb = MathF.Min(FloorDb, -0.001f);
            double share = Math.Pow(10, HoldHistoryTolerance * -floorDb / 20.0) - 1;
            double remainder = Math.Pow(10, floorDb / 20.0) / gain * share;
            if (remainder >= 1) return (0, smoothing);

            long frames = (long)Math.Ceiling(Math.Log(remainder) / Math.Log(smoothing));
            long limit = Math.Max(1, s_maxHoldHistory.ToFrame(frameRate, MidpointRounding.ToZero));
            return frames <= limit
                ? ((int)Math.Max(0, frames), smoothing)
                : ((int)limit, (float)Math.Pow(remainder, 1.0 / limit));
        }

        // Returns whether any bar changed.
        private bool Normalize(ReadOnlySpan<float> magnitudes, int fftSize)
        {
            float reference = fftSize * 0.5f;
            float gain = Math.Max(0f, Gain);
            // FloorDb は Range(-200, 0) だが、0 に設定されると (0 - floorDb) が 0 除算になるため -0.001 以下にクランプする
            float floorDb = MathF.Min(FloorDb, -0.001f);
            Span<float> normalized = _normalizedBars.AsSpan(0, magnitudes.Length);
            bool changed = false;
            for (int i = 0; i < magnitudes.Length; i++)
            {
                float db = Fft.MagnitudeToDb(magnitudes[i] * gain, reference);
                float n = Math.Clamp((db - floorDb) / (0f - floorDb), 0f, 1f);
                changed |= normalized[i] != n;
                normalized[i] = n;
            }

            return changed;
        }

        /// <summary>Runs a Hann-windowed FFT over the newest samples of <paramref name="samples"/> and returns the bin magnitudes.</summary>
        private Span<float> ComputeMagnitudes(ReadOnlySpan<float> samples, int fftSize, int bins)
        {
            if (_fftReal.Length < fftSize) _fftReal = new float[fftSize];
            if (_fftImag.Length < fftSize) _fftImag = new float[fftSize];
            if (_fftMagnitudes.Length < bins) _fftMagnitudes = new float[bins];
            Span<float> real = _fftReal.AsSpan(0, fftSize);
            Span<float> imag = _fftImag.AsSpan(0, fftSize);
            Span<float> mags = _fftMagnitudes.AsSpan(0, bins);

            real.Clear();
            imag.Clear();

            int sampleCount = samples.Length;
            int copy = Math.Min(sampleCount, fftSize);
            int srcStart = Math.Max(0, sampleCount - copy);
            int dstStart = fftSize - copy;
            samples.Slice(srcStart, copy).CopyTo(real.Slice(dstStart, copy));

            Fft.ApplyHann(real);
            Fft.Forward(real, imag);
            Fft.Magnitudes(real, imag, mags);
            return mags;
        }

        /// <summary>Resolves the bin range <c>[binLow, binHigh)</c> bar <paramref name="i"/> covers on the chosen scale.</summary>
        private static void ResolveBarBins(
            int i,
            int barCount,
            int bins,
            FrequencyScale freqScale,
            in FrequencyAxis axis,
            out int binLow,
            out int binHigh)
        {
            switch (freqScale)
            {
                case FrequencyScale.Logarithmic:
                    {
                        double freqLow = axis.FrequencyAt((double)i / barCount, freqScale);
                        double freqHigh = axis.FrequencyAt((double)(i + 1) / barCount, freqScale);
                        binLow = (int)Math.Floor(freqLow / axis.FMax * bins);
                        binHigh = (int)Math.Ceiling(freqHigh / axis.FMax * bins);
                        break;
                    }
                case FrequencyScale.Mel:
                    {
                        double m1 = axis.MelMin + (axis.MelMax - axis.MelMin) * i / barCount;
                        double m2 = axis.MelMin + (axis.MelMax - axis.MelMin) * (i + 1) / barCount;
                        double f1 = FrequencyAxis.MelToFrequency(m1);
                        double f2 = FrequencyAxis.MelToFrequency(m2);
                        binLow = (int)Math.Floor(f1 / axis.FMax * bins);
                        binHigh = (int)Math.Ceiling(f2 / axis.FMax * bins);
                        break;
                    }
                default:
                    binLow = i * bins / barCount;
                    binHigh = (i + 1) * bins / barCount;
                    break;
            }

            if (binHigh <= binLow) binHigh = binLow + 1;
            if (binHigh > bins) binHigh = bins;
            if (binLow < 0) binLow = 0;
        }

        private static float BandRms(ReadOnlySpan<float> mags, int binLow, int binHigh)
        {
            float sumSq = 0f;
            int count = binHigh - binLow;
            for (int b = binLow; b < binHigh; b++)
            {
                sumSq += mags[b] * mags[b];
            }
            return count > 0 ? MathF.Sqrt(sumSq / count) : 0f;
        }
    }
}
