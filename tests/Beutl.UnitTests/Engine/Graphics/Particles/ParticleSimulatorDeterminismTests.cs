using Beutl.Composition;
using Beutl.Graphics.Particles;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Particles;

[TestFixture]
public sealed class ParticleSimulatorDeterminismTests
{
    [TestCase(45, ParticlePreset.Near)]
    [TestCase(89, ParticlePreset.Near)]
    [TestCase(839, ParticlePreset.Near)]
    [TestCase(45, ParticlePreset.Far)]
    [TestCase(89, ParticlePreset.Far)]
    [TestCase(839, ParticlePreset.Far)]
    public void AbsoluteTimeSimulation_MatchesSequentialFrameEvaluation(
        int targetFrame,
        ParticlePreset preset)
    {
        const float frameRate = 30;
        var sequential = new ParticleSimulator();
        for (int frame = 0; frame <= targetFrame; frame++)
        {
            Simulate(sequential, frame / frameRate, preset);
        }

        var direct = new ParticleSimulator();
        Simulate(direct, targetFrame / frameRate, preset);

        Particle[] sequentialParticles = sequential.GetAliveParticles().ToArray();
        Particle[] directParticles = direct.GetAliveParticles().ToArray();
        Assert.That(
            sequentialParticles,
            Is.EqualTo(directParticles),
            "Particle state at an absolute timestamp must not depend on whether earlier export frames were evaluated.");
    }

    [TestCase(89, ParticlePreset.Near)]
    [TestCase(839, ParticlePreset.Near)]
    [TestCase(89, ParticlePreset.Far)]
    [TestCase(839, ParticlePreset.Far)]
    public void ResourceUpdate_AtExportTimestampMatchesColdSeek(
        int targetFrame,
        ParticlePreset preset)
    {
        const long frameRate = 30;
        var emitter = CreateEmitter(preset);
        using ParticleEmitter.Resource sequential = emitter.ToResource(new CompositionContext(TimeSpan.Zero));
        for (int frame = 1; frame <= targetFrame; frame++)
        {
            var updateOnly = false;
            sequential.Update(
                emitter,
                new CompositionContext(ExportTimestamp(frame, frameRate)),
                ref updateOnly);
        }

        using ParticleEmitter.Resource direct = emitter.ToResource(
            new CompositionContext(ExportTimestamp(targetFrame, frameRate)));
        Assert.That(
            sequential.GetAliveParticles().ToArray(),
            Is.EqualTo(direct.GetAliveParticles().ToArray()),
            "ParticleEmitter.Resource must preserve deterministic seek semantics at the exact export timestamp.");
    }

    [TestCase(89, ParticlePreset.Near)]
    [TestCase(839, ParticlePreset.Near)]
    [TestCase(89, ParticlePreset.Far)]
    [TestCase(839, ParticlePreset.Far)]
    public void RoundedStillTimestamp_MatchesExportFrame(
        int targetFrame,
        ParticlePreset preset)
    {
        const long frameRate = 30;
        float exportTime = (float)ExportTimestamp(targetFrame, frameRate).TotalSeconds;
        float stillTime = (float)Math.Round(targetFrame / (double)frameRate, 4);

        var export = new ParticleSimulator();
        Simulate(export, exportTime, preset);
        var still = new ParticleSimulator();
        Simulate(still, stillTime, preset);

        Assert.That(
            still.GetAliveParticles().ToArray(),
            Is.EqualTo(export.GetAliveParticles().ToArray()),
            "A decimal timestamp naming the same 30 fps frame must not introduce a query-specific partial simulation step.");
    }

    [TestCase(30)]
    [TestCase(60)]
    public void SyntheticEmitter_SequentialAndColdSeekStatesMatchThroughTenMinutes(int frameRate)
    {
        int[] ages = [1, 3, 10, 60, 300, 600];
        ParticleEmitter emitter = CreateSyntheticEmitter();
        using ParticleEmitter.Resource sequential =
            emitter.ToResource(new CompositionContext(TimeSpan.Zero));
        int previousFrame = 0;

        foreach (int age in ages)
        {
            int targetFrame = age * frameRate;
            for (int frame = previousFrame + 1; frame <= targetFrame; frame++)
            {
                var updateOnly = false;
                sequential.Update(
                    emitter,
                    new CompositionContext(ExportTimestamp(frame, frameRate)),
                    ref updateOnly);
            }

            using ParticleEmitter.Resource cold = emitter.ToResource(
                new CompositionContext(TimeSpan.FromSeconds(age)));
            Assert.That(
                sequential.GetAliveParticles().ToArray(),
                Is.EqualTo(cold.GetAliveParticles().ToArray()),
                $"Particle state at age {age}s and {frameRate} fps must be independent of evaluation history.");
            previousFrame = targetFrame;
        }
    }

    private static void Simulate(ParticleSimulator simulator, float time, ParticlePreset preset)
    {
        bool near = preset == ParticlePreset.Near;
        simulator.Simulate(
            time,
            seed: near ? 9203 : 4711,
            EmitterShape.Box,
            emitterWidth: near ? 2200 : 2400,
            emitterHeight: near ? 1200 : 1300,
            maxParticles: near ? 600 : 420,
            emissionRate: near ? 44 : 20,
            lifetime: near ? 6 : 9,
            lifetimeRandom: near ? 3 : 4,
            speed: near ? 34 : 16,
            speedRandom: near ? 22 : 10,
            direction: near ? 176 : 178,
            spread: near ? 16 : 22,
            gravity: near ? -4 : 0,
            airResistance: near ? 0.1f : 0.2f,
            turbulenceStrength: near ? 14 : 6,
            turbulenceScale: near ? 0.0026f : 0.0016f,
            turbulenceSpeed: near ? 0.4f : 0.25f,
            particleSize: near ? 2.2f : 9,
            sizeRandom: near ? 2.4f : 5,
            near ? new Color(255, 178, 193, 210) : new Color(255, 132, 161, 194),
            particleOpacity: 9,
            initialRotation: 0,
            initialRotationRandom: 0,
            angularVelocity: 0,
            endSizeMultiplier: near ? 0.6f : 1.2f,
            endOpacityMultiplier: 0,
            Colors.White,
            useEndColor: false);
    }

    private static ParticleEmitter CreateEmitter(ParticlePreset preset)
    {
        bool near = preset == ParticlePreset.Near;
        return new ParticleEmitter
        {
            Seed = { CurrentValue = near ? 9203 : 4711 },
            EmitterShape = { CurrentValue = EmitterShape.Box },
            EmitterWidth = { CurrentValue = near ? 2200 : 2400 },
            EmitterHeight = { CurrentValue = near ? 1200 : 1300 },
            MaxParticles = { CurrentValue = near ? 600 : 420 },
            EmissionRate = { CurrentValue = near ? 44 : 20 },
            Lifetime = { CurrentValue = near ? 6 : 9 },
            LifetimeRandom = { CurrentValue = near ? 3 : 4 },
            Speed = { CurrentValue = near ? 34 : 16 },
            SpeedRandom = { CurrentValue = near ? 22 : 10 },
            Direction = { CurrentValue = near ? 176 : 178 },
            Spread = { CurrentValue = near ? 16 : 22 },
            Gravity = { CurrentValue = near ? -4 : 0 },
            AirResistance = { CurrentValue = near ? 0.1f : 0.2f },
            TurbulenceStrength = { CurrentValue = near ? 14 : 6 },
            TurbulenceScale = { CurrentValue = near ? 0.0026f : 0.0016f },
            TurbulenceSpeed = { CurrentValue = near ? 0.4f : 0.25f },
            ParticleSize = { CurrentValue = near ? 2.2f : 9 },
            SizeRandom = { CurrentValue = near ? 2.4f : 5 },
            ParticleColor =
            {
                CurrentValue = near
                    ? new Color(255, 178, 193, 210)
                    : new Color(255, 132, 161, 194),
            },
            ParticleOpacity = { CurrentValue = 9 },
            EndSizeMultiplier = { CurrentValue = near ? 0.6f : 1.2f },
            EndOpacityMultiplier = { CurrentValue = 0 },
        };
    }

    private static ParticleEmitter CreateSyntheticEmitter()
    {
        return new ParticleEmitter
        {
            Seed = { CurrentValue = 12345 },
            EmitterShape = { CurrentValue = EmitterShape.Box },
            EmitterWidth = { CurrentValue = 320 },
            EmitterHeight = { CurrentValue = 180 },
            MaxParticles = { CurrentValue = 64 },
            EmissionRate = { CurrentValue = 120 },
            Lifetime = { CurrentValue = 2 },
            LifetimeRandom = { CurrentValue = 1 },
            Speed = { CurrentValue = 48 },
            SpeedRandom = { CurrentValue = 18 },
            Direction = { CurrentValue = 90 },
            Spread = { CurrentValue = 70 },
            Gravity = { CurrentValue = 12 },
            AirResistance = { CurrentValue = 0.08f },
            TurbulenceStrength = { CurrentValue = 16 },
            TurbulenceScale = { CurrentValue = 0.01f },
            TurbulenceSpeed = { CurrentValue = 0.5f },
            ParticleSize = { CurrentValue = 4 },
            SizeRandom = { CurrentValue = 2 },
            ParticleColor = { CurrentValue = Colors.White },
            ParticleOpacity = { CurrentValue = 100 },
            InitialRotationRandom = { CurrentValue = 180 },
            AngularVelocity = { CurrentValue = 24 },
            EndSizeMultiplier = { CurrentValue = 0.25f },
            EndOpacityMultiplier = { CurrentValue = 0 },
            EndColor = { CurrentValue = Colors.Transparent },
            UseEndColor = { CurrentValue = true },
        };
    }

    private static TimeSpan ExportTimestamp(long frame, long frameRate)
        => TimeSpan.FromTicks(frame * TimeSpan.TicksPerSecond / frameRate);

    public enum ParticlePreset
    {
        Near,
        Far,
    }
}
