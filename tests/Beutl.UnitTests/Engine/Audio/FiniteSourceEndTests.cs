using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Audio;
using Beutl.Audio.Effects;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Engine;
using Beutl.Validation;

namespace Beutl.UnitTests.Engine.Audio;

[TestFixture]
public class FiniteSourceEndTests
{
    private const int SampleRate = 48000;

    [Test]
    public void ShiftResampleGainAndSpeed_MapTheFiniteEndThroughTheGraph()
    {
        using var source = new FiniteSource(1);
        using var shift = new ShiftNode { Shift = TimeSpan.FromSeconds(0.25) };
        using var resample = new ResampleNode { SourceSampleRate = 44100 };
        using var gain = new GainNode { Gain = Property.CreateAnimatable(75f) };
        using var speed = new SpeedNode { Speed = Property.CreateAnimatable(200f) };
        shift.AddInput(source);
        resample.AddInput(shift);
        gain.AddInput(resample);
        speed.AddInput(gain);

        Assert.That(speed.GetFiniteSourceEndSample(SampleRate), Is.EqualTo(18000));
    }

    [Test]
    public void Mixer_UsesTheLatestFiniteEndAndPreservesUnknownBranches()
    {
        using var first = new FiniteSource(1);
        using var last = new FiniteSource(2);
        using var unknown = new UnknownSource();
        using var mixer = new MixerNode();
        mixer.AddInput(first);
        mixer.AddInput(last);
        Assert.That(mixer.GetFiniteSourceEndSample(SampleRate), Is.EqualTo(96000));

        mixer.AddInput(unknown);
        Assert.That(mixer.GetFiniteSourceEndSample(SampleRate), Is.Null);
    }

    [Test]
    public void Clip_ReportsItsOutputTimelineEnd()
    {
        using var clip = new ClipNode { Start = TimeSpan.FromSeconds(2), Duration = TimeSpan.FromSeconds(1) };
        using var source = new FiniteSource(0.5);
        clip.AddInput(source);

        Assert.That(clip.GetFiniteSourceEndSample(SampleRate), Is.EqualTo(120000));
    }

    [Test]
    public void Limiter_IncludesItsHeldSamplesAndDelayKeepsItsEchoTailUnbounded()
    {
        var effect = new LimiterEffect();
        effect.Lookahead.CurrentValue = 10;
        using var context = new AudioContext(SampleRate, 2);
        var source = context.AddNode(new FiniteSource(1));
        AudioNode limited = effect.CreateNode(context, source);
        Assert.That(limited.GetFiniteSourceEndSample(SampleRate), Is.EqualTo(48480));

        AudioNode delayed = new DelayEffect().CreateNode(context, limited);
        Assert.That(delayed.GetFiniteSourceEndSample(SampleRate), Is.Null);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void AnimatedSpeed_UsesTheInverseOfTheIntegratedMapping(bool preservePitch)
    {
        var effect = new TimeStretchEffect();
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = TimeSpan.Zero, Value = 100f });
        animation.KeyFrames.Add(new KeyFrame<float>
        {
            KeyTime = TimeSpan.FromSeconds(1),
            Value = 200f,
            Easing = new LinearEasing()
        });
        effect.Speed.Animation = animation;
        using var source = new FiniteSource(1.5);
        using var speed = new SpeedNode { Speed = effect.Speed, PreservePitch = preservePitch };
        speed.AddInput(source);

        Assert.That(speed.GetFiniteSourceEndSample(SampleRate), Is.EqualTo(48000).Within(1));
    }

    [Test]
    public void AnimatedEnd_IsCachedAndRecomputedAfterAnimationSourceOrRateChanges()
    {
        var animation = new CountingAnimation();
        var property = Property.CreateAnimatable(100f);
        property.Animation = animation;
        using var source = new MutableFiniteSource { Seconds = 0.125 };
        using var node = new SpeedNode { Speed = property };
        node.AddInput(source);
        double? first = node.GetFiniteSourceEndSample(SampleRate);
        int calls = animation.Calls;

        Assert.That(node.GetFiniteSourceEndSample(SampleRate), Is.EqualTo(first));
        Assert.That(animation.Calls, Is.EqualTo(calls), "An unchanged EOF query must not interpolate again.");

        animation.SetSpeed(200);
        Assert.That(node.GetFiniteSourceEndSample(SampleRate), Is.EqualTo(first / 2).Within(1));
        Assert.That(animation.Calls, Is.GreaterThan(calls));
        calls = animation.Calls;
        source.Seconds = 0.25;
        Assert.That(node.GetFiniteSourceEndSample(SampleRate), Is.EqualTo(first).Within(1));
        Assert.That(animation.Calls, Is.GreaterThan(calls));
        calls = animation.Calls;
        Assert.That(node.GetFiniteSourceEndSample(44100), Is.EqualTo(5512.5).Within(1));
        Assert.That(animation.Calls, Is.GreaterThan(calls));
    }

    [Test]
    public void TrimmedClip_FiniteEndIncludesHeldLatency()
    {
        var limiterEffect = new LimiterEffect();
        limiterEffect.Lookahead.CurrentValue = 10;
        using var context = new AudioContext(SampleRate, 2);
        var source = context.AddNode(new FiniteSource(2));
        AudioNode limiter = limiterEffect.CreateNode(context, source);
        var clip = context.AddNode(new ClipNode { Duration = TimeSpan.FromSeconds(1) });
        clip.AddInput(limiter);

        Assert.That(clip.GetFiniteSourceEndSample(SampleRate), Is.EqualTo(48480));
    }

    private sealed class MutableFiniteSource : AudioNode
    {
        public double Seconds { get; set; }
        internal override double? GetFiniteSourceEndSample(int sampleRate) => Seconds * sampleRate;
        public override AudioBuffer Process(AudioProcessContext context) => new(context.SampleRate, 2, context.GetSampleCount());
    }

    private sealed class CountingAnimation : Hierarchical, IAnimationRange<float>
    {
        public int Calls { get; private set; }
        private float _speed = 100;
        public TimeSpan Duration => TimeSpan.FromSeconds(1);
        public bool UseGlobalClock => false;
        public Type ValueType => typeof(float);
        public IValidator<float>? Validator { get; set; }
        public event EventHandler? Edited;
        public float GetAnimatedValue(TimeSpan time) => Interpolate(time);
        public float Interpolate(TimeSpan time) { Calls++; return _speed; }
        public bool TryGetOutputRange(out float minimum, out float maximum) { minimum = maximum = _speed; return true; }
        public void SetSpeed(float speed) { _speed = speed; Edited?.Invoke(this, EventArgs.Empty); }
    }

    private sealed class FiniteSource(double seconds) : AudioNode
    {
        internal override double? GetFiniteSourceEndSample(int sampleRate) => seconds * sampleRate;

        public override AudioBuffer Process(AudioProcessContext context)
            => new(context.SampleRate, 2, context.GetSampleCount());
    }

    private sealed class UnknownSource : AudioNode
    {
        public override AudioBuffer Process(AudioProcessContext context)
            => new(context.SampleRate, 2, context.GetSampleCount());
    }
}
