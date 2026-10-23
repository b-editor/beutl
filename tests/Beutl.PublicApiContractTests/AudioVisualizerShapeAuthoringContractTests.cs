using System.Diagnostics.CodeAnalysis;
using Beutl.Audio;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.AudioVisualizers;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Source;
using static Beutl.PublicApiContractTests.ContractTestSupport;

namespace Beutl.PublicApiContractTests;

[TestFixture]
public sealed class AudioVisualizerShapeAuthoringContractTests
{
    [OneTimeSetUp]
    public void OneTimeSetUp() => ToneDecoderInfo.EnsureRegistered();

    [Test]
    public void APluginAuthoredSpectrumShape_CanOverrideRender()
    {
        var bounds = new Rect(0, 0, 8, 4);
        using var fill = new SolidColorBrush.Resource { Color = Colors.White, Opacity = 100f };
        using var shape = new PluginSpectrumShape.Resource { Thickness = 4f };
        using var target = new CpuRenderTarget(new PixelSize(8, 4));

        using (var canvas = new ImmediateCanvas(target, RenderIntent.Preview))
        {
            canvas.Clear();
            shape.Draw(canvas, bounds, [0.5f, 1f], fill);
        }

        using Bitmap bitmap = target.Snapshot();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(shape.RenderCalls, Is.EqualTo(1));
            Assert.That(shape.ObservedBounds, Is.EqualTo(bounds));
            Assert.That(shape.ObservedValues, Is.EqualTo(new[] { 0.5f, 1f }));
            Assert.That(shape.ObservedFill, Is.SameAs(fill));
            Assert.That(shape.ObservedThickness, Is.EqualTo(4f));

            // The right slot is drawn full height, the left one only over its bottom half.
            Assert.That(AlphaAt(bitmap, 5, 0), Is.GreaterThan(0.9f));
            Assert.That(AlphaAt(bitmap, 1, 3), Is.GreaterThan(0.9f));
            Assert.That(AlphaAt(bitmap, 1, 0), Is.LessThan(0.01f));
        }
    }

    [Test]
    public void APluginAuthoredWaveformShape_CanOverrideRender()
    {
        var bounds = new Rect(0, 0, 8, 4);
        using var fill = new SolidColorBrush.Resource { Color = Colors.White, Opacity = 100f };
        using var shape = new PluginWaveformShape.Resource();
        using var target = new CpuRenderTarget(new PixelSize(8, 4));

        using (var canvas = new ImmediateCanvas(target, RenderIntent.Preview))
        {
            canvas.Clear();
            shape.Draw(canvas, bounds, [-1f, -0.5f], [1f, 0.5f], 1f, fill);
        }

        using Bitmap bitmap = target.Snapshot();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(shape.RenderCalls, Is.EqualTo(1));
            Assert.That(shape.ObservedBounds, Is.EqualTo(bounds));
            Assert.That(shape.ObservedMins, Is.EqualTo(new[] { -1f, -0.5f }));
            Assert.That(shape.ObservedMaxs, Is.EqualTo(new[] { 1f, 0.5f }));
            Assert.That(shape.ObservedGain, Is.EqualTo(1f));
            Assert.That(shape.ObservedFill, Is.SameAs(fill));

            // The left slot spans the full height, the right one only the middle band.
            Assert.That(AlphaAt(bitmap, 1, 0), Is.GreaterThan(0.9f));
            Assert.That(AlphaAt(bitmap, 5, 2), Is.GreaterThan(0.9f));
            Assert.That(AlphaAt(bitmap, 5, 0), Is.LessThan(0.01f));
        }
    }

    [Test]
    public void AnAudioSpectrumDrawable_MaterializesAPluginAuthoredShape()
    {
        var drawable = new AudioSpectrumDrawable();
        drawable.Shape.Value = new PluginSpectrumShape();

        using var resource = (AudioSpectrumDrawable.Resource)drawable.ToResource(CompositionContext.Default);

        Assert.That(resource.Shape, Is.InstanceOf<PluginSpectrumShape.Resource>());
    }

    [Test]
    public void AnAudioWaveformDrawable_MaterializesAPluginAuthoredShape()
    {
        var drawable = new AudioWaveformDrawable();
        drawable.Shape.Value = new PluginWaveformShape();

        using var resource = (AudioWaveformDrawable.Resource)drawable.ToResource(CompositionContext.Default);

        Assert.That(resource.Shape, Is.InstanceOf<PluginWaveformShape.Resource>());
    }

    [Test]
    public void APluginAuthoredVisualizer_ReadsTheSampleCacheAndPaints()
    {
        var currentTime = MediaTime.FromSeconds(0.5);
        var bounds = new Rect(0, 0, 8, 8);
        var drawable = new PluginVisualizerDrawable
        {
            Width = { Value = 8f },
            Height = { Value = 8f },
            Fill = { Value = new SolidColorBrush(Colors.White) },
            Source = { Value = CreateHalfScaleTone() },
        };

        using var resource = (PluginVisualizerDrawable.Resource)drawable.ToResource(
            new CompositionContext(currentTime));
        using var target = new CpuRenderTarget(new PixelSize(8, 8));

        using (var canvas = new ImmediateCanvas(target, RenderIntent.Preview))
        {
            canvas.Clear();
            resource.Draw(canvas, bounds);
        }

        using Bitmap bitmap = target.Snapshot();

        MediaTime expectedWindow = MediaTime.FromSeconds(
            (double)PluginVisualizerDrawable.WindowSampleCount / ToneDecoderInfo.SampleRate);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(resource.RenderCalls, Is.EqualTo(1));
            Assert.That(resource.ObservedSampleLength, Is.GreaterThan(0),
                "the synthetic tone composed no samples, so the paint below would be vacuous");
            Assert.That(resource.ObservedSampleSpanLength, Is.EqualTo(resource.ObservedSampleLength));
            Assert.That(resource.ObservedSampleRate, Is.EqualTo(ToneDecoderInfo.SampleRate));
            Assert.That(resource.ObservedComposerSampleRate, Is.EqualTo(ToneDecoderInfo.SampleRate));
            Assert.That(resource.ObservedStart, Is.EqualTo(currentTime));
            Assert.That(resource.ObservedDuration, Is.EqualTo(expectedWindow));
            Assert.That(resource.ObservedPeak, Is.EqualTo(ToneDecoderInfo.Amplitude).Within(0.001f));

            // The peak is half scale, so the level bar covers the bottom half and nothing above it.
            Assert.That(AlphaAt(bitmap, 4, 6), Is.GreaterThan(0.9f));
            Assert.That(AlphaAt(bitmap, 4, 1), Is.LessThan(0.01f));
        }
    }

    [Test]
    public void APluginAuthoredVisualizer_SizesItsWindowFromTheComposerRate()
    {
        var drawable = new PluginVisualizerDrawable();
        using var resource = (PluginVisualizerDrawable.Resource)drawable.ToResource(CompositionContext.Default);

        (MediaTime start, MediaTime duration) = resource.SampleWindow(MediaTime.FromSeconds(2));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(start, Is.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That(duration, Is.EqualTo(MediaTime.FromSeconds(
                (double)PluginVisualizerDrawable.WindowSampleCount / ToneDecoderInfo.SampleRate)));
        }
    }

    private static SourceSound CreateHalfScaleTone()
    {
        var soundSource = new SoundSource();
        soundSource.ReadFrom(new Uri(ToneDecoderInfo.CreateToneFile()));
        return new SourceSound
        {
            Source = { Value = soundSource },
            // Sound.Compose clips to TimeRange, so the range has to cover the sample window.
            TimeRange = { Value = new TimeRange(MediaTime.Zero, MediaTime.FromSeconds(2)) },
        };
    }

}

public sealed partial class PluginSpectrumShape : SpectrumShape
{
    public PluginSpectrumShape()
    {
        ScanProperties<PluginSpectrumShape>();
    }

    public IExpressionProperty<float> Thickness { get; } = Property.CreateAnimatable(4f);

    public partial class Resource
    {
        public int RenderCalls { get; private set; }

        public Rect ObservedBounds { get; private set; }

        public float[] ObservedValues { get; private set; } = [];

        public float ObservedThickness { get; private set; }

        public Brush.Resource? ObservedFill { get; private set; }

        public void Draw(
            ImmediateCanvas canvas,
            Rect bounds,
            ReadOnlySpan<float> normalizedBars,
            Brush.Resource fill)
            => Render(new SpectrumRenderContext(canvas, bounds, normalizedBars, fill));

        protected override void Render(in SpectrumRenderContext context)
        {
            RenderCalls++;
            ObservedBounds = context.Bounds;
            ObservedValues = context.NormalizedBars.ToArray();
            ObservedThickness = Thickness;
            ObservedFill = context.Fill;

            ReadOnlySpan<float> normalizedBars = context.NormalizedBars;
            int count = normalizedBars.Length;
            if (count == 0) return;

            Rect bounds = context.Bounds;
            float slotWidth = (float)bounds.Width / count;
            float barWidth = MathF.Min(Thickness, slotWidth);
            for (int i = 0; i < count; i++)
            {
                float barHeight = normalizedBars[i] * (float)bounds.Height;
                context.Canvas.DrawRectangle(
                    new Rect(
                        (float)bounds.X + i * slotWidth,
                        (float)bounds.Bottom - barHeight,
                        barWidth,
                        barHeight),
                    context.Fill,
                    null);
            }
        }
    }
}

public sealed partial class PluginWaveformShape : WaveformShape
{
    public PluginWaveformShape()
    {
        ScanProperties<PluginWaveformShape>();
    }

    public partial class Resource
    {
        public int RenderCalls { get; private set; }

        public Rect ObservedBounds { get; private set; }

        public float[] ObservedMins { get; private set; } = [];

        public float[] ObservedMaxs { get; private set; } = [];

        public float ObservedGain { get; private set; }

        public Brush.Resource? ObservedFill { get; private set; }

        public void Draw(
            ImmediateCanvas canvas,
            Rect bounds,
            ReadOnlySpan<float> mins,
            ReadOnlySpan<float> maxs,
            float gain,
            Brush.Resource fill)
            => Render(new WaveformRenderContext(canvas, bounds, mins, maxs, gain, fill));

        protected override void Render(in WaveformRenderContext context)
        {
            RenderCalls++;
            ObservedBounds = context.Bounds;
            ObservedMins = context.Mins.ToArray();
            ObservedMaxs = context.Maxs.ToArray();
            ObservedGain = context.Gain;
            ObservedFill = context.Fill;

            ReadOnlySpan<float> mins = context.Mins;
            ReadOnlySpan<float> maxs = context.Maxs;
            int count = Math.Min(mins.Length, maxs.Length);
            if (count == 0) return;

            Rect bounds = context.Bounds;
            float gain = context.Gain;
            float slotWidth = (float)bounds.Width / count;
            float half = (float)bounds.Height * 0.5f;
            float centerY = (float)bounds.Y + half;
            for (int i = 0; i < count; i++)
            {
                float top = centerY - Math.Clamp(maxs[i] * gain, -1f, 1f) * half;
                float bottom = centerY - Math.Clamp(mins[i] * gain, -1f, 1f) * half;
                context.Canvas.DrawRectangle(
                    new Rect((float)bounds.X + i * slotWidth, top, slotWidth, bottom - top),
                    context.Fill,
                    null);
            }
        }
    }
}

/// <summary>
/// A level meter of the kind a plugin author would write: it picks its own sample window and paints from the
/// engine's sample cache, so it compiles only while those accessors are reachable from a derived type.
/// </summary>
public sealed partial class PluginVisualizerDrawable : AudioVisualizerDrawable
{
    public const int WindowSampleCount = 1024;

    public PluginVisualizerDrawable()
    {
        ScanProperties<PluginVisualizerDrawable>();
    }

    public partial class Resource
    {
        public int RenderCalls { get; private set; }

        public int ObservedSampleLength { get; private set; }

        public int ObservedSampleSpanLength { get; private set; }

        public int ObservedSampleRate { get; private set; }

        public int ObservedComposerSampleRate { get; private set; }

        public MediaTime ObservedStart { get; private set; }

        public MediaTime ObservedDuration { get; private set; }

        public float ObservedPeak { get; private set; }

        public void Draw(ImmediateCanvas canvas, Rect bounds) => RenderForeground(canvas, bounds);

        public (MediaTime Start, MediaTime Duration) SampleWindow(MediaTime currentTime)
            => ComputeSampleWindow(currentTime);

        protected override (MediaTime Start, MediaTime Duration) ComputeSampleWindow(MediaTime currentTime)
            => (currentTime, MediaTime.FromSeconds((double)WindowSampleCount / ComposerSampleRate));

        protected override void RenderForeground(ImmediateCanvas canvas, Rect bounds)
        {
            RenderCalls++;
            ObservedSampleLength = CachedSampleLength;
            ObservedSampleRate = CachedSampleRate;
            ObservedComposerSampleRate = ComposerSampleRate;
            ObservedStart = CachedStart;
            ObservedDuration = CachedDuration;

            ReadOnlySpan<float> samples = CachedSampleSpan;
            ObservedSampleSpanLength = samples.Length;

            float peak = 0f;
            foreach (float sample in samples)
            {
                peak = MathF.Max(peak, MathF.Abs(sample));
            }

            ObservedPeak = peak;

            if (Fill is not { } fill) return;

            float level = Math.Clamp(peak * Gain, 0f, 1f);
            float height = (float)bounds.Height * level;
            if (height <= 0f) return;

            canvas.DrawRectangle(
                new Rect((float)bounds.X, (float)bounds.Bottom - height, (float)bounds.Width, height),
                fill,
                null);
        }
    }
}

/// <summary>
/// Decodes a made-up extension into a steady half-scale tone, so a visualizer under test composes a
/// non-empty, exactly predictable sample cache without depending on a real codec.
/// </summary>
internal sealed class ToneDecoderInfo : IDecoderInfo
{
    public const int SampleRate = 44100;
    public const int Channels = 2;
    public const float Amplitude = 0.5f;

    private const string Extension = ".contracttone";
    private static readonly Lock s_gate = new();
    private static bool s_registered;

    public string Name => "Contract-test tone";

    public static void EnsureRegistered()
    {
        lock (s_gate)
        {
            if (s_registered) return;
            DecoderRegistry.Register(new ToneDecoderInfo());
            s_registered = true;
        }
    }

    public static string CreateToneFile()
    {
        string path = Path.Combine(Path.GetTempPath(), $"beutl-contract-tone{Extension}");
        if (!File.Exists(path))
        {
            File.WriteAllBytes(path, []);
        }

        return path;
    }

    public MediaReader? Open(string file, MediaOptions options)
        => IsSupported(file) ? new ToneReader() : null;

    public bool IsSupported(string file)
        => Path.GetExtension(file).Equals(Extension, StringComparison.OrdinalIgnoreCase);

    public IEnumerable<string> VideoExtensions() => [];

    public IEnumerable<string> AudioExtensions() => [Extension];

    private sealed class ToneReader : MediaReader
    {
        public override VideoStreamInfo VideoInfo { get; } =
            new("contract-tone", 0, default, new Rational(1, 1));

        public override AudioStreamInfo AudioInfo { get; } = new(
            "contract-tone",
            new Rational(TimeSpan.TicksPerSecond * 2, TimeSpan.TicksPerSecond),
            SampleRate,
            Channels);

        public override bool HasVideo => false;

        public override bool HasAudio => true;

        public override bool ReadVideo(int frame, [NotNullWhen(true)] out Ref<Bitmap>? image)
        {
            image = null;
            return false;
        }

        public override bool ReadAudio(int start, int length, [NotNullWhen(true)] out AudioBuffer? sound)
        {
            if (length <= 0)
            {
                sound = null;
                return false;
            }

            sound = new AudioBuffer(SampleRate, 2, length);
            sound.GetChannelData(0).Fill(Amplitude);
            sound.GetChannelData(1).Fill(Amplitude);
            return true;
        }
    }
}
