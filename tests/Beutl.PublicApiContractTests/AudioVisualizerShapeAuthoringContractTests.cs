using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.AudioVisualizers;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.PublicApiContractTests;

/// <summary>
/// This project is not a friend of <c>Beutl.Engine</c>, so the shapes below compile only while the audio
/// visualizer draw hook stays reachable from a type derived outside the engine assembly. Each shape splits
/// into an abstract plugin base that only forwards to the hook and a concrete plugin that overrides it, so
/// the forwarding call site is bound to the base declaration and reaches the override through the vtable.
/// </summary>
[TestFixture]
public sealed class AudioVisualizerShapeAuthoringContractTests
{
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
        drawable.Shape.CurrentValue = new PluginSpectrumShape();

        using var resource = (AudioSpectrumDrawable.Resource)drawable.ToResource(CompositionContext.Default);

        Assert.That(resource.Shape, Is.InstanceOf<PluginSpectrumShape.Resource>());
    }

    [Test]
    public void AnAudioWaveformDrawable_MaterializesAPluginAuthoredShape()
    {
        var drawable = new AudioWaveformDrawable();
        drawable.Shape.CurrentValue = new PluginWaveformShape();

        using var resource = (AudioWaveformDrawable.Resource)drawable.ToResource(CompositionContext.Default);

        Assert.That(resource.Shape, Is.InstanceOf<PluginWaveformShape.Resource>());
    }

    private static float AlphaAt(Bitmap bitmap, int x, int y)
    {
        Assert.That(bitmap.ColorType, Is.EqualTo(BitmapColorType.RgbaF16));
        return (float)bitmap.GetRow<Half>(y)[x * 4 + 3];
    }

    private sealed class CpuRenderTarget : RenderTarget
    {
        private static readonly SKColorSpace s_colorSpace = SKColorSpace.CreateSrgbLinear();

        public CpuRenderTarget(PixelSize size)
            : base(CreateSurface(size), size.Width, size.Height)
        {
        }

        private static SKSurface CreateSurface(PixelSize size)
        {
            return SKSurface.Create(new SKImageInfo(
                       size.Width,
                       size.Height,
                       SKColorType.RgbaF16,
                       SKAlphaType.Premul,
                       s_colorSpace))
                   ?? throw new InvalidOperationException("Could not create a CPU contract-test surface.");
        }
    }
}

/// <summary>
/// Only forwards to the engine's draw hook; it never overrides it, so the call in
/// <see cref="Resource.Draw"/> is bound to <c>SpectrumShape.Resource.Render</c> itself.
/// </summary>
public abstract partial class PluginSpectrumShapeBase : SpectrumShape
{
    public abstract partial class Resource
    {
        public void Draw(
            ImmediateCanvas canvas,
            Rect bounds,
            ReadOnlySpan<float> normalizedBars,
            Brush.Resource fill)
            => Render(canvas, bounds, normalizedBars, fill);
    }
}

public sealed partial class PluginSpectrumShape : PluginSpectrumShapeBase
{
    public PluginSpectrumShape()
    {
        ScanProperties<PluginSpectrumShape>();
    }

    public IProperty<float> Thickness { get; } = Property.CreateAnimatable(4f);

    public partial class Resource
    {
        public int RenderCalls { get; private set; }

        public Rect ObservedBounds { get; private set; }

        public float[] ObservedValues { get; private set; } = [];

        public float ObservedThickness { get; private set; }

        public Brush.Resource? ObservedFill { get; private set; }

        protected override void Render(
            ImmediateCanvas canvas,
            Rect bounds,
            ReadOnlySpan<float> normalizedBars,
            Brush.Resource fill)
        {
            RenderCalls++;
            ObservedBounds = bounds;
            ObservedValues = normalizedBars.ToArray();
            ObservedThickness = Thickness;
            ObservedFill = fill;

            int count = normalizedBars.Length;
            if (count == 0) return;

            float slotWidth = (float)bounds.Width / count;
            float barWidth = MathF.Min(Thickness, slotWidth);
            for (int i = 0; i < count; i++)
            {
                float barHeight = normalizedBars[i] * (float)bounds.Height;
                canvas.DrawRectangle(
                    new Rect(
                        (float)bounds.X + i * slotWidth,
                        (float)bounds.Bottom - barHeight,
                        barWidth,
                        barHeight),
                    fill,
                    null);
            }
        }
    }
}

/// <summary>
/// Only forwards to the engine's draw hook; it never overrides it, so the call in
/// <see cref="Resource.Draw"/> is bound to <c>WaveformShape.Resource.Render</c> itself.
/// </summary>
public abstract partial class PluginWaveformShapeBase : WaveformShape
{
    public abstract partial class Resource
    {
        public void Draw(
            ImmediateCanvas canvas,
            Rect bounds,
            ReadOnlySpan<float> mins,
            ReadOnlySpan<float> maxs,
            float gain,
            Brush.Resource fill)
            => Render(canvas, bounds, mins, maxs, gain, fill);
    }
}

public sealed partial class PluginWaveformShape : PluginWaveformShapeBase
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

        protected override void Render(
            ImmediateCanvas canvas,
            Rect bounds,
            ReadOnlySpan<float> mins,
            ReadOnlySpan<float> maxs,
            float gain,
            Brush.Resource fill)
        {
            RenderCalls++;
            ObservedBounds = bounds;
            ObservedMins = mins.ToArray();
            ObservedMaxs = maxs.ToArray();
            ObservedGain = gain;
            ObservedFill = fill;

            int count = Math.Min(mins.Length, maxs.Length);
            if (count == 0) return;

            float slotWidth = (float)bounds.Width / count;
            float half = (float)bounds.Height * 0.5f;
            float centerY = (float)bounds.Y + half;
            for (int i = 0; i < count; i++)
            {
                float top = centerY - Math.Clamp(maxs[i] * gain, -1f, 1f) * half;
                float bottom = centerY - Math.Clamp(mins[i] * gain, -1f, 1f) * half;
                canvas.DrawRectangle(
                    new Rect((float)bounds.X + i * slotWidth, top, slotWidth, bottom - top),
                    fill,
                    null);
            }
        }
    }
}
