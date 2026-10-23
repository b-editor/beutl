using System.Globalization;
using Beutl.Animation;
using Beutl.Composition;
using Beutl.Graphics.Particles;
using Beutl.Media;
using Beutl.Serialization;

namespace Beutl.UnitTests.Engine.Graphics.Particles;

[TestFixture]
public sealed class ParticlePrewarmTests
{
    [TestCase("00:01:00.0000001")]
    [TestCase("01:00:00")]
    [TestCase("2")]
    [TestCase("500.00:00:00")]
    [TestCase("10675199.02:48:05.4775807")]
    public void UnsupportedDuration_IsRejectedBeforeSimulation(string text)
    {
        var emitter = CreateEmitter();
        // As a file reads the text: the last case is past the range of MediaTime and clamps to its maximum.
        emitter.PrewarmDuration.Value = MediaTime.FromTicks(
            TimeSpan.Parse(text, CultureInfo.InvariantCulture).Ticks, MidpointRounding.ToPositiveInfinity);

        // Assert before evaluating the resource so the regression cannot run days of simulation.
        Assert.That(emitter.PrewarmDuration.Value, Is.EqualTo(MediaTime.Zero));
        using var resource = emitter.ToResource(new CompositionContext(MediaTime.Zero));
        Assert.That(resource.GetAliveParticles().Length, Is.Zero);
    }

    [TestCase("00:00:59.9999999")]
    [TestCase("00:01:00")]
    public void SupportedDuration_AtTheUpperBoundaryIsPreserved(string text)
    {
        var emitter = CreateEmitter();
        var duration = MediaTime.FromTimeSpan(TimeSpan.Parse(text, CultureInfo.InvariantCulture));
        emitter.PrewarmDuration.Value = duration;

        Assert.That(emitter.PrewarmDuration.Value, Is.EqualTo(duration));
        using var resource = emitter.ToResource(new CompositionContext(MediaTime.Zero));
        Assert.That(resource.GetAliveParticles().Length, Is.GreaterThan(0));
    }

    [Test]
    public void SerializedUnsupportedDuration_DoesNotOverflowOnTheFirstFrame()
    {
        var json = CoreSerializer.SerializeToJsonObject(CreateEmitter());
        json[nameof(ParticleEmitter.PrewarmDuration)] = "500.00:00:00";
        var restored = (ParticleEmitter)CoreSerializer.DeserializeFromJsonObject(json, typeof(ParticleEmitter));

        Assert.DoesNotThrow(() =>
        {
            using var resource = restored.ToResource(new CompositionContext(MediaTime.Zero));
            Assert.That(resource.GetAliveParticles().Length, Is.Zero);
        });
        Assert.That(restored.PrewarmDuration.Value, Is.EqualTo(MediaTime.Zero));
    }

    [TestCase(0.125, 0)]
    [TestCase(2, 0)]
    [TestCase(2, 7)]
    public void PrewarmAtStart_MatchesElapsedSimulation(double duration, double start)
    {
        var emitter = CreateEmitter();
        using var expected = emitter.ToResource(new CompositionContext(MediaTime.FromSeconds(duration)));
        emitter.PrewarmDuration.Value = MediaTime.FromSeconds(duration);
        emitter.TimeRange.Value = new TimeRange(MediaTime.FromSeconds(start), MediaTime.FromSeconds(10));
        using var actual = emitter.ToResource(new CompositionContext(emitter.Start));

        Assert.That(actual.GetAliveParticles().Length, Is.GreaterThan(0));
        Assert.That(actual.GetAliveParticles().ToArray(), Is.EqualTo(expected.GetAliveParticles().ToArray()));
    }

    [TestCase(0)]
    [TestCase(-2)]
    public void NonPositiveDuration_PreservesUnwarmedBehavior(double duration)
    {
        var emitter = CreateEmitter();
        using var expected = emitter.ToResource(new CompositionContext(MediaTime.FromSeconds(1)));
        emitter.PrewarmDuration.Value = MediaTime.FromSeconds(duration);
        using var actual = emitter.ToResource(new CompositionContext(MediaTime.Zero));
        Assert.That(actual.GetAliveParticles().Length, Is.Zero);

        Update(actual, emitter, MediaTime.FromSeconds(1));
        Assert.That(actual.GetAliveParticles().ToArray(), Is.EqualTo(expected.GetAliveParticles().ToArray()));
    }

    [Test]
    public void BeforeStart_RemainsEmptyEvenWithPrewarm()
    {
        var emitter = CreateEmitter();
        emitter.TimeRange.Value = new TimeRange(MediaTime.FromSeconds(7), MediaTime.FromSeconds(10));
        emitter.PrewarmDuration.Value = MediaTime.FromSeconds(2);
        using var resource = emitter.ToResource(new CompositionContext(emitter.Start));
        Assert.That(resource.GetAliveParticles().Length, Is.GreaterThan(0));

        Update(resource, emitter, emitter.Start - MediaTime.FromTimeSpan(TimeSpan.FromTicks(1)));
        Assert.That(resource.GetAliveParticles().Length, Is.Zero);
    }

    [TestCase(30)]
    [TestCase(60)]
    public void SequentialFrames_MatchColdSeekAndUnwarmedSimulation(int frameRate)
    {
        var emitter = CreateEmitter();
        emitter.PrewarmDuration.Value = MediaTime.FromSeconds(2.125);
        using var sequential = emitter.ToResource(new CompositionContext(MediaTime.Zero));
        MediaTime time = default;
        for (int frame = 1; frame <= 89; frame++)
        {
            time = MediaTime.FromTimeSpan(TimeSpan.FromTicks((long)frame * TimeSpan.TicksPerSecond / frameRate));
            Update(sequential, emitter, time);
        }

        using var cold = emitter.ToResource(new CompositionContext(time));
        var unwarmed = CreateEmitter();
        using var expected = unwarmed.ToResource(new CompositionContext(time + emitter.PrewarmDuration.Value));
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
        emitter.PrewarmDuration.Value = MediaTime.FromSeconds(2);
        using var reused = emitter.ToResource(new CompositionContext(MediaTime.Zero));
        foreach (int seconds in new[] { 1, 70, 2, 0, 10 })
        {
            var time = MediaTime.FromSeconds(seconds);
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
        emitter.PrewarmDuration.Value = MediaTime.FromSeconds(2);
        using var resource = emitter.ToResource(new CompositionContext(MediaTime.Zero));
        var before = resource.GetAliveParticles().ToArray();
        int version = resource.Version;
        emitter.PrewarmDuration.Value = MediaTime.FromSeconds(duration);
        Update(resource, emitter, MediaTime.Zero);
        using var cold = emitter.ToResource(new CompositionContext(MediaTime.Zero));

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
        emitter.PrewarmDuration.Value = MediaTime.FromSeconds(2);
        using var resource = emitter.ToResource(new CompositionContext(MediaTime.Zero));
        var before = resource.GetAliveParticles().ToArray();
        emitter.Seed.Value++;
        Update(resource, emitter, MediaTime.Zero);
        using var cold = emitter.ToResource(new CompositionContext(MediaTime.Zero));

        Assert.That(resource.GetAliveParticles().ToArray(), Is.Not.EqualTo(before));
        Assert.That(resource.GetAliveParticles().ToArray(), Is.EqualTo(cold.GetAliveParticles().ToArray()));
    }

    [Test]
    public void Prewarm_DoesNotAdvancePropertyAnimationsOrTheCompositionClock()
    {
        var emitter = CreateEmitter();
        emitter.PrewarmDuration.Value = MediaTime.FromSeconds(2);
        emitter.Speed.SetKeyFrameAnimation(new KeyFrameAnimation<float>
        {
            KeyFrames =
            {
                new KeyFrame<float> { KeyTime = { Value = MediaTime.Zero }, Value = { Value = 20 } },
                new KeyFrame<float> { KeyTime = { Value = MediaTime.FromSeconds(2) }, Value = { Value = 200 } },
            },
        });
        var context = new CompositionContext(MediaTime.Zero);
        using var actual = emitter.ToResource(context);
        using var expected = CreateEmitter().ToResource(new CompositionContext(MediaTime.FromSeconds(2)));

        Assert.That(context.Time, Is.EqualTo(MediaTime.Zero));
        Assert.That(actual.GetAliveParticles().ToArray(), Is.EqualTo(expected.GetAliveParticles().ToArray()));
    }

    [Test]
    public void SerializedPrewarmDuration_StartsWithTheSimulatedParticleState()
    {
        var emitter = CreateEmitter();
        using var expected = emitter.ToResource(new CompositionContext(MediaTime.FromSeconds(2)));
        var json = CoreSerializer.SerializeToJsonObject(emitter);
        json["PrewarmDuration"] = "00:00:02";
        var restored = (ParticleEmitter)CoreSerializer.DeserializeFromJsonObject(json, typeof(ParticleEmitter));
        using var actual = restored.ToResource(new CompositionContext(MediaTime.Zero));

        Assert.That(expected.GetAliveParticles().Length, Is.GreaterThan(0));
        Assert.That(actual.GetAliveParticles().ToArray(), Is.EqualTo(expected.GetAliveParticles().ToArray()));
    }

    [Test]
    public void Serialization_PreservesDurationAndDefaultsMissingPropertyToZero()
    {
        var emitter = CreateEmitter();
        emitter.PrewarmDuration.Value = MediaTime.FromSeconds(2.125);
        var json = CoreSerializer.SerializeToJsonObject(emitter);
        var restored = (ParticleEmitter)CoreSerializer.DeserializeFromJsonObject(json, typeof(ParticleEmitter));
        Assert.That(restored.PrewarmDuration.Value, Is.EqualTo(emitter.PrewarmDuration.Value));

        json.Remove(nameof(ParticleEmitter.PrewarmDuration));
        var legacy = (ParticleEmitter)CoreSerializer.DeserializeFromJsonObject(json, typeof(ParticleEmitter));
        Assert.That(legacy.PrewarmDuration.Value, Is.EqualTo(MediaTime.Zero));
        using var resource = legacy.ToResource(new CompositionContext(MediaTime.Zero));
        Assert.That(resource.GetAliveParticles().Length, Is.Zero);
    }

    private static void Update(ParticleEmitter.Resource resource, ParticleEmitter emitter, MediaTime time)
    {
        bool versionBumped = false;
        resource.Reconcile(emitter, new CompositionContext(time), ref versionBumped);
    }

    private static ParticleEmitter CreateEmitter()
    {
        return new ParticleEmitter
        {
            Seed = { Value = 2112 },
            EmitterShape = { Value = EmitterShape.Line },
            EmitterWidth = { Value = 320 },
            EmissionRate = { Value = 60 },
            Lifetime = { Value = 4 },
            Speed = { Value = 20 },
            SpeedRandom = { Value = 5 },
            Direction = { Value = 90 },
            Spread = { Value = 5 },
            Gravity = { Value = 10 },
            TurbulenceStrength = { Value = 2 },
        };
    }
}
