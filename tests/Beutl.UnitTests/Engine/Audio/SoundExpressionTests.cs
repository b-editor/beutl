using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Beutl.Audio;
using Beutl.Audio.Composing;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.UnitTests.Engine.Graphics.Rendering;

namespace Beutl.UnitTests.Engine.Audio;

// The audio graph read the stored value of a sound's gain, speed and offset, so an expression on them was silently
// ignored (#3608). An expression runs a script, so it must not run for every sample either.
[TestFixture]
public class SoundExpressionTests
{
    private const int SampleRate = 44100;

    [OneTimeSetUp]
    public void OneTimeSetUp() => TestMediaHelper.RegisterTestDecoder();

    [Test]
    public void Gain_PlaysTheExpressionValue()
    {
        var expression = new CountingExpression<float>(50f);
        float[] expected = Compose(sound => sound.Gain.Value = 50f);
        float[] actual = Compose(sound => sound.Gain.Expression = expression);

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.EqualTo(expected).Within(1e-6f));
            // A grid of 100 points a second, not one evaluation for each of the 44100 samples.
            Assert.That(expression.Evaluations, Is.InRange(100, 120));
        });
    }

    [Test]
    public void Speed_PlaysAtTheExpressionValue()
    {
        var expression = new CountingExpression<float>(200f);
        float[] expected = Compose(sound => sound.Speed.Value = 200f);
        float[] actual = Compose(sound => sound.Speed.Expression = expression);

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.EqualTo(expected).Within(1e-6f));
            Assert.That(expression.Evaluations, Is.InRange(1, 4));
        });
    }

    [Test]
    public void OffsetPosition_StartsAtTheExpressionValue()
    {
        var expression = new CountingExpression<MediaTime>(MediaTime.FromSeconds(0.25));
        float[] expected = Compose(sound => sound.OffsetPosition.Value = MediaTime.FromSeconds(0.25));
        float[] actual = Compose(sound => sound.OffsetPosition.Expression = expression);

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.EqualTo(expected).Within(1e-6f));
            Assert.That(expression.Evaluations, Is.InRange(1, 4));
        });
    }

    // A speed or offset an expression gives is known for one window only, and a time stretch asked for the source's
    // end before the first window evaluated it, so the stored value cut the input short.
    [Test]
    public void Expression_LeavesTheSourceEndUnknown()
    {
        IExpressionProperty<float> speedProperty = Property.CreateAnimatable(100f);
        speedProperty.Expression = new CountingExpression<float>(50f);
        using var speed = new Beutl.Audio.Graph.AudioSpeedMapping { Speed = speedProperty };
        IExpressionProperty<MediaTime> offset = Property.CreateExpression<MediaTime>();
        offset.Expression = new CountingExpression<MediaTime>(MediaTime.FromSeconds(0.25));
        using var shift = new Beutl.Audio.Graph.Nodes.ShiftNode { Offset = offset };

        Assert.Multiple(() =>
        {
            Assert.That(speed.GetFiniteSourceEndSample(SampleRate, SampleRate), Is.Null);
            Assert.That(shift.GetFiniteSourceEndSample(SampleRate), Is.Null);
        });
    }

    private static float[] Compose(Action<SourceSound> configure)
    {
        string path = TestMediaHelper.CreateTestAudioFile(sampleRate: SampleRate, channels: 2, durationSeconds: 2.0);
        var source = new SoundSource();
        source.ReadFrom(new Uri(path));
        var range = new TimeRange(MediaTime.Zero, MediaTime.FromSeconds(1));
        var sound = new SourceSound { Source = { Value = source }, TimeRange = { Value = range } };
        configure(sound);
        using var resource = sound.ToResource(CompositionContext.Default);
        var frame = new CompositionFrame(
            ImmutableArray.Create((EngineObject.Resource)resource), range, default, new CompositionEligibility([sound]));
        using var composer = new Composer { SampleRate = SampleRate };
        using AudioBuffer? buffer = composer.Compose(range, frame);
        return buffer!.GetChannelData(0).ToArray();
    }

    private sealed class CountingExpression<T>(T value) : IExpression<T>
    {
        public int Evaluations { get; private set; }

        public string ExpressionString => value?.ToString() ?? string.Empty;

        public T Evaluate(ExpressionContext context)
        {
            Evaluations++;
            return value;
        }

        public bool Validate([NotNullWhen(false)] out string? error)
        {
            error = null;
            return true;
        }
    }
}
