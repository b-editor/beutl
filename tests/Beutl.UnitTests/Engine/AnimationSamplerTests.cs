using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Engine;
using Beutl.Media;

namespace Beutl.UnitTests.Engine;

internal sealed partial class AnimationSamplerTestRoot : EngineObject, IHierarchicalRoot
{
    public event EventHandler<IHierarchical>? DescendantAttached;
    public event EventHandler<IHierarchical>? DescendantDetached;

    public void OnDescendantAttached(IHierarchical descendant)
        => DescendantAttached?.Invoke(this, descendant);

    public void OnDescendantDetached(IHierarchical descendant)
        => DescendantDetached?.Invoke(this, descendant);
}

[TestFixture]
public class AnimationSamplerTests
{
    private static IProperty<float> AnimatedFloat(float from, float to, double durationSeconds)
    {
        var property = Property.CreateAnimatable(from);
        property.SetAttributes("Value", []);

        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float>
        {
            KeyTime = TimeSpan.Zero,
            Value = from,
            Easing = new LinearEasing()
        });
        animation.KeyFrames.Add(new KeyFrame<float>
        {
            KeyTime = TimeSpan.FromSeconds(durationSeconds),
            Value = to,
            Easing = new LinearEasing()
        });
        property.Animation = animation;
        return property;
    }

    private static (IProperty<float> Property, EngineObject Owner) AttachedAnimatedFloat(
        float from, float to, double durationSeconds, TimeRange ownerTimeRange, bool useGlobalClock)
    {
        var owner = new AnimationSamplerTestRoot { TimeRange = ownerTimeRange };

        var property = Property.CreateAnimatable(from);
        property.SetAttributes("Value", []);

        var animation = new KeyFrameAnimation<float> { UseGlobalClock = useGlobalClock };
        animation.KeyFrames.Add(new KeyFrame<float>
        {
            KeyTime = TimeSpan.Zero,
            Value = from,
            Easing = new LinearEasing()
        });
        animation.KeyFrames.Add(new KeyFrame<float>
        {
            KeyTime = TimeSpan.FromSeconds(durationSeconds),
            Value = to,
            Easing = new LinearEasing()
        });
        property.Animation = animation;
        property.SetOwnerObject(owner);
        return (property, owner);
    }

    [Test]
    public void SampleBuffer_NullProperty_Throws()
    {
        var sampler = new AnimationSampler();
        Span<float> buffer = stackalloc float[8];

        // Need to use an indirect call because of stackalloc + lambda issue
        Assert.Throws<ArgumentNullException>(() => Invoke(sampler));

        static void Invoke(AnimationSampler sampler)
        {
            Span<float> b = stackalloc float[8];
            sampler.SampleBuffer<float>(null!, new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)), 8, b);
        }
    }

    [Test]
    public void SampleBuffer_NoAnimation_FillsWithCurrentValue()
    {
        var sampler = new AnimationSampler();
        var property = Property.CreateAnimatable(3.5f);
        property.SetAttributes("Value", []);
        property.CurrentValue = 9.5f;

        Span<float> buffer = stackalloc float[4];
        sampler.SampleBuffer(property, new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)), 4, buffer);

        for (int i = 0; i < buffer.Length; i++)
        {
            Assert.That(buffer[i], Is.EqualTo(9.5f));
        }
    }

    [Test]
    public void SampleBuffer_KeyFrameAnimation_ProducesIncreasingSamples()
    {
        var sampler = new AnimationSampler();
        var property = AnimatedFloat(0f, 10f, 1.0);

        Span<float> buffer = stackalloc float[10];
        sampler.SampleBuffer(property, new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)), 10, buffer);

        Assert.That(buffer[0], Is.EqualTo(0f).Within(1e-3));
        // Linearly interpolated => values should monotonically increase.
        for (int i = 1; i < buffer.Length; i++)
        {
            Assert.That(buffer[i], Is.GreaterThanOrEqualTo(buffer[i - 1]));
        }
    }

    [Test]
    public void SampleBuffer_RangeOffsetMovesStartingValue()
    {
        var sampler = new AnimationSampler();
        var property = AnimatedFloat(0f, 100f, 1.0);

        Span<float> buffer = stackalloc float[1];
        sampler.SampleBuffer(property, new TimeRange(TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(0.5)), 4, buffer);

        Assert.That(buffer[0], Is.EqualTo(50f).Within(1f));
    }

    [Test]
    public void SampleBuffer_EmptyOutput_DoesNothing()
    {
        var sampler = new AnimationSampler();
        var property = AnimatedFloat(0f, 10f, 1.0);

        Assert.DoesNotThrow(() => sampler.SampleBuffer(
            property, new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)), 4, Span<float>.Empty));
    }

    [Test]
    public void SampleBuffer_RespectsElementLocalTime_WhenUseGlobalClockIsFalse()
    {
        var sampler = new AnimationSampler();
        var (property, _) = AttachedAnimatedFloat(
            from: 0f,
            to: 100f,
            durationSeconds: 1.0,
            ownerTimeRange: new TimeRange(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1)),
            useGlobalClock: false);

        Span<float> buffer = stackalloc float[10];
        sampler.SampleBuffer(
            property,
            new TimeRange(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1)),
            10,
            buffer);

        Assert.That(buffer[0], Is.EqualTo(0f).Within(1e-3));
        Assert.That(buffer[^1], Is.EqualTo(90f).Within(1e-3));
        for (int i = 1; i < buffer.Length; i++)
        {
            Assert.That(buffer[i], Is.GreaterThan(buffer[i - 1]));
        }
    }

    [Test]
    public void SampleBuffer_UsesGlobalTime_WhenUseGlobalClockIsTrue()
    {
        var sampler = new AnimationSampler();
        var (property, _) = AttachedAnimatedFloat(
            from: 0f,
            to: 100f,
            durationSeconds: 1.0,
            ownerTimeRange: new TimeRange(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1)),
            useGlobalClock: true);

        Span<float> buffer = stackalloc float[10];
        sampler.SampleBuffer(
            property,
            new TimeRange(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1)),
            10,
            buffer);

        for (int i = 0; i < buffer.Length; i++)
        {
            Assert.That(buffer[i], Is.EqualTo(100f).Within(1e-3));
        }
    }
}
