using Beutl.Animation;
using Beutl.Composition;
using Beutl.Graphics.Particles;
using Beutl.Media;
using Beutl.Serialization;

namespace Beutl.UnitTests.Engine.Graphics.Particles;

[TestFixture]
public sealed class ParticlePrewarmTests
{
    [TestCase(0.125, 0)]
    [TestCase(2, 0)]
    [TestCase(2, 7)]
    public void PrewarmAtStart_MatchesElapsedSimulation(double duration, double start)
    {
        var emitter = CreateEmitter();
        using var expected = emitter.ToResource(new CompositionContext(TimeSpan.FromSeconds(duration)));
        emitter.PrewarmDuration.CurrentValue = TimeSpan.FromSeconds(duration);
        emitter.TimeRange = new TimeRange(TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(10));
        using var actual = emitter.ToResource(new CompositionContext(emitter.Start));

        Assert.That(actual.GetAliveParticles().Length, Is.GreaterThan(0));
        Assert.That(actual.GetAliveParticles().ToArray(), Is.EqualTo(expected.GetAliveParticles().ToArray()));
    }

    [TestCase(0)]
    [TestCase(-2)]
    public void NonPositiveDuration_PreservesUnwarmedBehavior(double duration)
    {
        var emitter = CreateEmitter();
        using var expected = emitter.ToResource(new CompositionContext(TimeSpan.FromSeconds(1)));
        emitter.PrewarmDuration.CurrentValue = TimeSpan.FromSeconds(duration);
        using var actual = emitter.ToResource(new CompositionContext(TimeSpan.Zero));
        Assert.That(actual.GetAliveParticles().Length, Is.Zero);

        Update(actual, emitter, TimeSpan.FromSeconds(1));
        Assert.That(actual.GetAliveParticles().ToArray(), Is.EqualTo(expected.GetAliveParticles().ToArray()));
    }

    [Test]
    public void BeforeStart_RemainsEmptyEvenWithPrewarm()
    {
        var emitter = CreateEmitter();
        emitter.TimeRange = new TimeRange(TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(10));
        emitter.PrewarmDuration.CurrentValue = TimeSpan.FromSeconds(2);
        using var resource = emitter.ToResource(new CompositionContext(emitter.Start));
        Assert.That(resource.GetAliveParticles().Length, Is.GreaterThan(0));

        Update(resource, emitter, emitter.Start - TimeSpan.FromTicks(1));
        Assert.That(resource.GetAliveParticles().Length, Is.Zero);
    }

    [TestCase(30)]
    [TestCase(60)]
    public void SequentialFrames_MatchColdSeekAndUnwarmedSimulation(int frameRate)
    {
        var emitter = CreateEmitter();
        emitter.PrewarmDuration.CurrentValue = TimeSpan.FromSeconds(2.125);
        using var sequential = emitter.ToResource(new CompositionContext(TimeSpan.Zero));
        TimeSpan time = default;
        for (int frame = 1; frame <= 89; frame++)
        {
            time = TimeSpan.FromTicks((long)frame * TimeSpan.TicksPerSecond / frameRate);
            Update(sequential, emitter, time);
        }

        using var cold = emitter.ToResource(new CompositionContext(time));
        var unwarmed = CreateEmitter();
        using var expected = unwarmed.ToResource(new CompositionContext(time + emitter.PrewarmDuration.CurrentValue));
        Assert.Multiple(() =>
        {
            Assert.That(sequential.GetAliveParticles().ToArray(), Is.EqualTo(cold.GetAliveParticles().ToArray()));
            Assert.That(sequential.GetAliveParticles().ToArray(), Is.EqualTo(expected.GetAliveParticles().ToArray()));
        });
    }

    [Test]
    public void ForwardAndBackwardSeeks_MatchColdStateAfterCheckpointEviction()
    {
        var emitter = CreateEmitter();
        emitter.PrewarmDuration.CurrentValue = TimeSpan.FromSeconds(2);
        using var reused = emitter.ToResource(new CompositionContext(TimeSpan.Zero));
        foreach (int seconds in new[] { 1, 70, 2, 0, 10 })
        {
            var time = TimeSpan.FromSeconds(seconds);
            Update(reused, emitter, time);
            using var cold = emitter.ToResource(new CompositionContext(time));
            Assert.That(reused.GetAliveParticles().ToArray(), Is.EqualTo(cold.GetAliveParticles().ToArray()),
                $"Seek to {seconds}s must not depend on cached frames.");
        }
    }

    [TestCase(0)]
    [TestCase(0.25)]
    [TestCase(5)]
    public void ChangingDurationAtSameTime_UpdatesParticlesAndRenderVersion(double duration)
    {
        var emitter = CreateEmitter();
        emitter.PrewarmDuration.CurrentValue = TimeSpan.FromSeconds(2);
        using var resource = emitter.ToResource(new CompositionContext(TimeSpan.Zero));
        var before = resource.GetAliveParticles().ToArray();
        int version = resource.Version;
        emitter.PrewarmDuration.CurrentValue = TimeSpan.FromSeconds(duration);
        Update(resource, emitter, TimeSpan.Zero);
        using var cold = emitter.ToResource(new CompositionContext(TimeSpan.Zero));

        Assert.Multiple(() =>
        {
            Assert.That(resource.Version, Is.GreaterThan(version));
            Assert.That(resource.GetAliveParticles().ToArray(), Is.Not.EqualTo(before));
            Assert.That(resource.GetAliveParticles().ToArray(), Is.EqualTo(cold.GetAliveParticles().ToArray()));
        });
    }

    [Test]
    public void ChangingSeed_RebuildsThePrewarmedState()
    {
        var emitter = CreateEmitter();
        emitter.PrewarmDuration.CurrentValue = TimeSpan.FromSeconds(2);
        using var resource = emitter.ToResource(new CompositionContext(TimeSpan.Zero));
        var before = resource.GetAliveParticles().ToArray();
        emitter.Seed.CurrentValue++;
        Update(resource, emitter, TimeSpan.Zero);
        using var cold = emitter.ToResource(new CompositionContext(TimeSpan.Zero));

        Assert.That(resource.GetAliveParticles().ToArray(), Is.Not.EqualTo(before));
        Assert.That(resource.GetAliveParticles().ToArray(), Is.EqualTo(cold.GetAliveParticles().ToArray()));
    }

    [Test]
    public void Prewarm_DoesNotAdvancePropertyAnimationsOrTheCompositionClock()
    {
        var emitter = CreateEmitter();
        emitter.PrewarmDuration.CurrentValue = TimeSpan.FromSeconds(2);
        emitter.Speed.Animation = new KeyFrameAnimation<float>
        {
            KeyFrames =
            {
                new KeyFrame<float> { KeyTime = TimeSpan.Zero, Value = 20 },
                new KeyFrame<float> { KeyTime = TimeSpan.FromSeconds(2), Value = 200 },
            },
        };
        var context = new CompositionContext(TimeSpan.Zero);
        using var actual = emitter.ToResource(context);
        using var expected = CreateEmitter().ToResource(new CompositionContext(TimeSpan.FromSeconds(2)));

        Assert.That(context.Time, Is.EqualTo(TimeSpan.Zero));
        Assert.That(actual.GetAliveParticles().ToArray(), Is.EqualTo(expected.GetAliveParticles().ToArray()));
    }

    [Test]
    public void SerializedPrewarmDuration_StartsWithTheSimulatedParticleState()
    {
        var emitter = CreateEmitter();
        using var expected = emitter.ToResource(new CompositionContext(TimeSpan.FromSeconds(2)));
        var json = CoreSerializer.SerializeToJsonObject(emitter);
        json["PrewarmDuration"] = "00:00:02";
        var restored = (ParticleEmitter)CoreSerializer.DeserializeFromJsonObject(json, typeof(ParticleEmitter));
        using var actual = restored.ToResource(new CompositionContext(TimeSpan.Zero));

        Assert.That(expected.GetAliveParticles().Length, Is.GreaterThan(0));
        Assert.That(actual.GetAliveParticles().ToArray(), Is.EqualTo(expected.GetAliveParticles().ToArray()));
    }

    [Test]
    public void Serialization_PreservesDurationAndDefaultsMissingPropertyToZero()
    {
        var emitter = CreateEmitter();
        emitter.PrewarmDuration.CurrentValue = TimeSpan.FromSeconds(2.125);
        var json = CoreSerializer.SerializeToJsonObject(emitter);
        var restored = (ParticleEmitter)CoreSerializer.DeserializeFromJsonObject(json, typeof(ParticleEmitter));
        Assert.That(restored.PrewarmDuration.CurrentValue, Is.EqualTo(emitter.PrewarmDuration.CurrentValue));

        json.Remove(nameof(ParticleEmitter.PrewarmDuration));
        var legacy = (ParticleEmitter)CoreSerializer.DeserializeFromJsonObject(json, typeof(ParticleEmitter));
        Assert.That(legacy.PrewarmDuration.CurrentValue, Is.EqualTo(TimeSpan.Zero));
        using var resource = legacy.ToResource(new CompositionContext(TimeSpan.Zero));
        Assert.That(resource.GetAliveParticles().Length, Is.Zero);
    }

    private static void Update(ParticleEmitter.Resource resource, ParticleEmitter emitter, TimeSpan time)
    {
        bool updateOnly = false;
        resource.Update(emitter, new CompositionContext(time), ref updateOnly);
    }

    private static ParticleEmitter CreateEmitter()
    {
        return new ParticleEmitter
        {
            Seed = { CurrentValue = 2112 },
            EmitterShape = { CurrentValue = EmitterShape.Line },
            EmitterWidth = { CurrentValue = 320 },
            EmissionRate = { CurrentValue = 60 },
            Lifetime = { CurrentValue = 4 },
            Speed = { CurrentValue = 20 },
            SpeedRandom = { CurrentValue = 5 },
            Direction = { CurrentValue = 90 },
            Spread = { CurrentValue = 5 },
            Gravity = { CurrentValue = 10 },
            TurbulenceStrength = { CurrentValue = 2 },
        };
    }
}
