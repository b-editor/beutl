using System.Runtime.InteropServices;

using Beutl.Graphics.Effects;
using Beutl.Media;

namespace Beutl.Graphics.Particles;

internal sealed class ParticleSimulator
{
    private const float FixedDeltaTime = 1f / 60f;
    // One TimeSpan tick (100 ns) is at most 6e-6 of a 60 Hz step, so 8e-6 absorbs frame-grid
    // truncation while genuine near-boundary timestamps from off-60 rational rates (a 60001/1000
    // fps frame sits ~1.7e-5 steps early) are never snapped.
    private const double TickTruncationStepTolerance = 8e-6d;
    private const double MaximumSnapTolerance = 2.5e-5d;
    private const int CheckpointIntervalSteps = 30;
    private const int MaxCheckpoints = 120;

    private readonly PerlinNoise _noise = new();

    private Particle[] _particles = new Particle[256];
    private int _aliveCount;

    // Checkpoint cache
    private readonly List<(int Step, Particle[] Snapshot, int AliveCount, int RngCallCount)> _checkpoints = [];
    private long _parameterVersion;
    private long _lastCachedVersion;

    public void InvalidateCache()
    {
        _parameterVersion++;
    }

    public void Simulate(double time, in ParticleSettings settings)
    {
        if (time <= 0)
        {
            _aliveCount = 0;
            return;
        }

        // Check if parameter version changed - invalidate checkpoints
        if (_lastCachedVersion != _parameterVersion)
        {
            _checkpoints.Clear();
            _lastCachedVersion = _parameterVersion;
        }

        int targetStep = ResolveTargetStep(time);
        RestoreNearestCheckpoint(targetStep, out int currentStep, out float currentTime, out int rngSkipCount);

        var rng = new CountingRandom(settings.Seed, rngSkipCount);

        while (currentStep < targetStep)
        {
            EmitParticles(in settings, rng, currentTime, FixedDeltaTime);
            IntegrateParticles(in settings, currentTime, FixedDeltaTime);
            currentStep++;
            // Deriving the time from the step keeps birth times and turbulence phases on the
            // canonical timeline; accumulating float deltas drifts ~102 ms over ten minutes.
            currentTime = (float)(currentStep * (double)FixedDeltaTime);
            if (currentStep % CheckpointIntervalSteps == 0)
            {
                SaveCheckpoint(currentStep, rng.CallCount);
            }
        }

        // Particle state is defined only at canonical fixed steps. Advancing a query-specific
        // remainder would make equivalent frame timestamps such as 89 / 30 and 2.9667 produce
        // different state and consume an extra random sample.
    }

    /// <summary>
    /// Loads the nearest canonical fixed-step checkpoint at or before <paramref name="targetStep"/>, or the empty
    /// initial state when none is retained, and drops the checkpoints a backward seek has invalidated.
    /// </summary>
    private void RestoreNearestCheckpoint(int targetStep, out int currentStep, out float currentTime, out int rngSkipCount)
    {
        currentStep = 0;
        currentTime = 0;
        _aliveCount = 0;
        rngSkipCount = 0;
        int checkpointIndex = -1;

        for (int i = _checkpoints.Count - 1; i >= 0; i--)
        {
            if (_checkpoints[i].Step <= targetStep)
            {
                checkpointIndex = i;
                currentStep = _checkpoints[i].Step;
                currentTime = (float)(currentStep * (double)FixedDeltaTime);
                Particle[] snapshot = _checkpoints[i].Snapshot;
                _aliveCount = _checkpoints[i].AliveCount;
                rngSkipCount = _checkpoints[i].RngCallCount;
                EnsureCapacity(snapshot.Length);
                Array.Copy(snapshot, _particles, snapshot.Length);
                break;
            }
        }

        if (checkpointIndex >= 0)
        {
            // Remove checkpoints after this step in case of a backward seek.
            _checkpoints.RemoveRange(checkpointIndex + 1, _checkpoints.Count - checkpointIndex - 1);
        }
        else if (_checkpoints.Count > 0)
        {
            // The requested time predates the oldest retained checkpoint.
            _checkpoints.Clear();
        }
    }

    /// <summary>Emits the particles born during the step that starts at <paramref name="currentTime"/>.</summary>
    private void EmitParticles(in ParticleSettings settings, CountingRandom rng, float currentTime, float dt)
    {
        float emitCount = settings.EmissionRate * dt;
        int toEmit = (int)emitCount;
        float frac = emitCount - toEmit;
        if (rng.NextSingle() < frac)
            toEmit++;

        float directionRadians = settings.Direction * MathF.PI / 180f;
        float spreadRadians = settings.Spread * MathF.PI / 180f;
        for (int i = 0; i < toEmit; i++)
        {
            if (_aliveCount >= settings.MaxParticles)
                break;

            EnsureCapacity(_aliveCount + 1);

            ref Particle p = ref _particles[_aliveCount];
            p.BirthTime = currentTime;
            p.Lifetime = settings.Lifetime + (rng.NextSingle() * 2f - 1f) * settings.LifetimeRandom;
            if (p.Lifetime < 0.01f) p.Lifetime = 0.01f;

            SpawnPosition(rng, settings.EmitterShape, settings.EmitterWidth, settings.EmitterHeight, out p.X, out p.Y);

            float spd = settings.Speed + (rng.NextSingle() * 2f - 1f) * settings.SpeedRandom;
            float angle = directionRadians + (rng.NextSingle() * 2f - 1f) * spreadRadians;
            p.VelocityX = MathF.Cos(angle) * spd;
            p.VelocityY = MathF.Sin(angle) * spd;

            p.BaseSize = settings.ParticleSize + (rng.NextSingle() * 2f - 1f) * settings.SizeRandom;
            if (p.BaseSize < 0) p.BaseSize = 0;

            p.BaseOpacity = settings.ParticleOpacity;
            p.BaseColor = settings.ParticleColor;

            p.Rotation = settings.InitialRotation + (rng.NextSingle() * 2f - 1f) * settings.InitialRotationRandom;
            p.AngularVelocity = settings.AngularVelocity;

            _aliveCount++;
        }
    }

    /// <summary>Ages, moves and restyles every live particle across the step that starts at <paramref name="currentTime"/>.</summary>
    private void IntegrateParticles(in ParticleSettings settings, float currentTime, float dt)
    {
        Span<Particle> span = _particles.AsSpan(0, _aliveCount);
        for (int i = span.Length - 1; i >= 0; i--)
        {
            ref Particle p = ref span[i];

            float age = currentTime + dt - p.BirthTime;
            if (age >= p.Lifetime)
            {
                // Swap with last alive
                if (i < _aliveCount - 1)
                {
                    span[i] = span[_aliveCount - 1];
                }
                _aliveCount--;
                continue;
            }

            p.VelocityY += settings.Gravity * dt;

            if (settings.AirResistance > 0)
            {
                float factor = 1f - settings.AirResistance * dt;
                if (factor < 0) factor = 0;
                p.VelocityX *= factor;
                p.VelocityY *= factor;
            }

            if (settings.TurbulenceStrength > 0)
            {
                float nx = _noise.Perlin(
                    p.X * settings.TurbulenceScale + currentTime * settings.TurbulenceSpeed,
                    p.Y * settings.TurbulenceScale + settings.Seed);
                float ny = _noise.Perlin(
                    p.Y * settings.TurbulenceScale + settings.Seed,
                    p.X * settings.TurbulenceScale + currentTime * settings.TurbulenceSpeed);
                p.VelocityX += (nx - 0.5f) * 2f * settings.TurbulenceStrength * dt;
                p.VelocityY += (ny - 0.5f) * 2f * settings.TurbulenceStrength * dt;
            }

            p.X += p.VelocityX * dt;
            p.Y += p.VelocityY * dt;

            p.Rotation += p.AngularVelocity * dt;

            float t = age / p.Lifetime;
            p.CurrentSize = p.BaseSize * (1f + (settings.EndSizeMultiplier - 1f) * t);
            p.CurrentOpacity = p.BaseOpacity * (1f + (settings.EndOpacityMultiplier - 1f) * t);
            if (p.CurrentOpacity < 0) p.CurrentOpacity = 0;

            if (settings.UseEndColor)
            {
                p.CurrentColor = LerpColor(p.BaseColor, settings.EndColor, t);
            }
            else
            {
                p.CurrentColor = p.BaseColor;
            }
        }
    }

    public ReadOnlyMemory<Particle> GetAliveParticles()
    {
        return _particles.AsMemory(0, _aliveCount);
    }

    internal static int ResolveTargetStep(double time)
    {
        double timeUlp = Math.Max(
            Math.Abs(Math.BitIncrement(time) - time),
            Math.Abs(time - Math.BitDecrement(time)));
        return SnapToStep(time * 60d, timeUlp, MaximumSnapTolerance);
    }

    /// <summary>
    /// Snaps <paramref name="stepPosition"/> to the nearest whole step when it lies within the rounding error its
    /// timestamp can carry, capped at <paramref name="maxTolerance"/>, and returns the step it falls in.
    /// </summary>
    private static int SnapToStep(double stepPosition, double timeUlp, double maxTolerance)
    {
        double nearestStep = Math.Round(stepPosition);
        double arithmeticUlp = Math.Abs(Math.BitIncrement(stepPosition) - stepPosition);
        // Frame timestamps are truncated to 100 ns ticks before they reach the simulator.
        // One tick is at most 6e-6 of a 60 Hz step; keep a fixed margin in addition to
        // the float-relative tolerance so early timestamps snap as reliably as later ones.
        double snapTolerance = Math.Min(
            maxTolerance,
            Math.Max(
                TickTruncationStepTolerance,
                (timeUlp * 60d) + (arithmeticUlp * 2d)));
        if (Math.Abs(stepPosition - nearestStep) <= snapTolerance)
        {
            stepPosition = nearestStep;
        }

        return checked((int)Math.Floor(stepPosition));
    }

    private void SaveCheckpoint(int step, int rngCallCount)
    {
        var snapshot = new Particle[_aliveCount];
        Array.Copy(_particles, snapshot, _aliveCount);
        _checkpoints.Add((step, snapshot, _aliveCount, rngCallCount));

        if (_checkpoints.Count > MaxCheckpoints)
        {
            _checkpoints.RemoveAt(0);
        }
    }

    private void EnsureCapacity(int required)
    {
        if (_particles.Length >= required) return;
        int newSize = Math.Max(_particles.Length * 2, required);
        Array.Resize(ref _particles, newSize);
    }

    private static void SpawnPosition(CountingRandom rng, EmitterShape shape, float width, float height, out float x, out float y)
    {
        switch (shape)
        {
            case EmitterShape.Line:
                x = (rng.NextSingle() - 0.5f) * width;
                y = 0;
                break;
            case EmitterShape.Circle:
                {
                    float radius = width / 2f;
                    float r = MathF.Sqrt(rng.NextSingle()) * radius;
                    float angle = rng.NextSingle() * MathF.PI * 2f;
                    x = MathF.Cos(angle) * r;
                    y = MathF.Sin(angle) * r;
                    break;
                }
            case EmitterShape.Box:
                x = (rng.NextSingle() - 0.5f) * width;
                y = (rng.NextSingle() - 0.5f) * height;
                break;
            default: // Point
                x = 0;
                y = 0;
                break;
        }
    }

    private static Color LerpColor(Color a, Color b, float t)
    {
        return new Color(
            (byte)(a.A + (b.A - a.A) * t),
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));
    }

    private sealed class CountingRandom
    {
        private readonly Random _rng;

        public CountingRandom(int seed, int skipCount = 0)
        {
            _rng = new Random(seed);
            CallCount = skipCount;
            for (int i = 0; i < skipCount; i++)
                _rng.NextSingle();
        }

        public int CallCount { get; private set; }

        public float NextSingle()
        {
            CallCount++;
            return _rng.NextSingle();
        }
    }
}
