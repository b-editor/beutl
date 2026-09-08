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
    private readonly List<(int Step, float Time, Particle[] Snapshot, int AliveCount, int RngCallCount)> _checkpoints = [];
    private long _parameterVersion;
    private long _lastCachedVersion;

    public void InvalidateCache()
    {
        _parameterVersion++;
    }

    public void Simulate(
        double time,
        int seed,
        EmitterShape emitterShape,
        float emitterWidth,
        float emitterHeight,
        int maxParticles,
        float emissionRate,
        float lifetime,
        float lifetimeRandom,
        float speed,
        float speedRandom,
        float direction,
        float spread,
        float gravity,
        float airResistance,
        float turbulenceStrength,
        float turbulenceScale,
        float turbulenceSpeed,
        float particleSize,
        float sizeRandom,
        Color color,
        float particleOpacity,
        float initialRotation,
        float initialRotationRandom,
        float angularVelocity,
        float endSizeMultiplier,
        float endOpacityMultiplier,
        Color endColor,
        bool useEndColor)
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

        // Find the nearest canonical fixed-step checkpoint before the requested time.
        int currentStep = 0;
        float currentTime = 0;
        _aliveCount = 0;
        int rngSkipCount = 0;
        int checkpointIndex = -1;

        for (int i = _checkpoints.Count - 1; i >= 0; i--)
        {
            if (_checkpoints[i].Step <= targetStep)
            {
                checkpointIndex = i;
                currentStep = _checkpoints[i].Step;
                currentTime = _checkpoints[i].Time;
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

        var rng = new CountingRandom(seed, rngSkipCount);

        float dirRad = direction * MathF.PI / 180f;
        float spreadRad = spread * MathF.PI / 180f;

        void Advance(float dt)
        {
            // Emit particles
            float emitCount = emissionRate * dt;
            int toEmit = (int)emitCount;
            float frac = emitCount - toEmit;
            if (rng.NextSingle() < frac)
                toEmit++;

            for (int i = 0; i < toEmit; i++)
            {
                if (_aliveCount >= maxParticles)
                    break;

                EnsureCapacity(_aliveCount + 1);

                ref Particle p = ref _particles[_aliveCount];
                p.BirthTime = currentTime;
                p.Lifetime = lifetime + (rng.NextSingle() * 2f - 1f) * lifetimeRandom;
                if (p.Lifetime < 0.01f) p.Lifetime = 0.01f;

                // Emitter shape position
                SpawnPosition(rng, emitterShape, emitterWidth, emitterHeight, out p.X, out p.Y);

                // Velocity
                float spd = speed + (rng.NextSingle() * 2f - 1f) * speedRandom;
                float angle = dirRad + (rng.NextSingle() * 2f - 1f) * spreadRad;
                p.VelocityX = MathF.Cos(angle) * spd;
                p.VelocityY = MathF.Sin(angle) * spd;

                // Size
                p.BaseSize = particleSize + (rng.NextSingle() * 2f - 1f) * sizeRandom;
                if (p.BaseSize < 0) p.BaseSize = 0;

                p.BaseOpacity = particleOpacity;
                p.BaseColor = color;

                // Rotation
                p.Rotation = initialRotation + (rng.NextSingle() * 2f - 1f) * initialRotationRandom;
                p.AngularVelocity = angularVelocity;

                p.IsAlive = true;
                _aliveCount++;
            }

            // Update particles
            Span<Particle> span = _particles.AsSpan(0, _aliveCount);
            for (int i = span.Length - 1; i >= 0; i--)
            {
                ref Particle p = ref span[i];
                if (!p.IsAlive) continue;

                float age = currentTime + dt - p.BirthTime;
                if (age >= p.Lifetime)
                {
                    p.IsAlive = false;
                    // Swap with last alive
                    if (i < _aliveCount - 1)
                    {
                        span[i] = span[_aliveCount - 1];
                    }
                    _aliveCount--;
                    continue;
                }

                // Gravity
                p.VelocityY += gravity * dt;

                // Air resistance
                if (airResistance > 0)
                {
                    float factor = 1f - airResistance * dt;
                    if (factor < 0) factor = 0;
                    p.VelocityX *= factor;
                    p.VelocityY *= factor;
                }

                // Turbulence
                if (turbulenceStrength > 0)
                {
                    float nx = _noise.Perlin(
                        p.X * turbulenceScale + currentTime * turbulenceSpeed,
                        p.Y * turbulenceScale + seed);
                    float ny = _noise.Perlin(
                        p.Y * turbulenceScale + seed,
                        p.X * turbulenceScale + currentTime * turbulenceSpeed);
                    p.VelocityX += (nx - 0.5f) * 2f * turbulenceStrength * dt;
                    p.VelocityY += (ny - 0.5f) * 2f * turbulenceStrength * dt;
                }

                // Position integration
                p.X += p.VelocityX * dt;
                p.Y += p.VelocityY * dt;

                // Rotation
                p.Rotation += p.AngularVelocity * dt;

                // Over-life interpolation
                float t = age / p.Lifetime;
                p.CurrentSize = p.BaseSize * (1f + (endSizeMultiplier - 1f) * t);
                p.CurrentOpacity = p.BaseOpacity * (1f + (endOpacityMultiplier - 1f) * t);
                if (p.CurrentOpacity < 0) p.CurrentOpacity = 0;

                if (useEndColor)
                {
                    p.CurrentColor = LerpColor(p.BaseColor, endColor, t);
                }
                else
                {
                    p.CurrentColor = p.BaseColor;
                }
            }
        }

        while (currentStep < targetStep)
        {
            Advance(FixedDeltaTime);
            currentStep++;
            // Deriving the time from the step keeps birth times and turbulence phases on the
            // canonical timeline; accumulating float deltas drifts ~102 ms over ten minutes.
            currentTime = (float)(currentStep * (double)FixedDeltaTime);
            if (currentStep % CheckpointIntervalSteps == 0)
            {
                SaveCheckpoint(currentStep, currentTime, rng.CallCount);
            }
        }

        // Particle state is defined only at canonical fixed steps. Advancing a query-specific
        // remainder would make equivalent frame timestamps such as 89 / 30 and 2.9667 produce
        // different state and consume an extra random sample.
    }

    public ReadOnlyMemory<Particle> GetAliveParticles()
    {
        return _particles.AsMemory(0, _aliveCount);
    }

    internal static int ResolveTargetStep(float time)
    {
        double stepPosition = (double)time * 60d;
        double nearestStep = Math.Round(stepPosition);
        double timeUlp = Math.Max(
            Math.Abs((double)MathF.BitIncrement(time) - time),
            Math.Abs((double)time - MathF.BitDecrement(time)));
        double arithmeticUlp = Math.Abs(Math.BitIncrement(stepPosition) - stepPosition);
        // Frame timestamps are truncated to 100 ns ticks before they reach the simulator.
        // One tick is at most 6e-6 of a 60 Hz step; keep a fixed margin in addition to
        // the float-relative tolerance so early timestamps snap as reliably as later ones.
        double snapTolerance = Math.Min(
            0.25d,
            Math.Max(
                TickTruncationStepTolerance,
                (timeUlp * 60d) + (arithmeticUlp * 2d)));
        if (Math.Abs(stepPosition - nearestStep) <= snapTolerance)
        {
            stepPosition = nearestStep;
        }

        return checked((int)Math.Floor(stepPosition));
    }

    internal static int ResolveTargetStep(double time)
    {
        double stepPosition = time * 60d;
        double nearestStep = Math.Round(stepPosition);
        double timeUlp = Math.Max(
            Math.Abs(Math.BitIncrement(time) - time),
            Math.Abs(time - Math.BitDecrement(time)));
        double arithmeticUlp = Math.Abs(Math.BitIncrement(stepPosition) - stepPosition);
        double snapTolerance = Math.Min(
            MaximumSnapTolerance,
            Math.Max(
                TickTruncationStepTolerance,
                (timeUlp * 60d) + (arithmeticUlp * 2d)));
        if (Math.Abs(stepPosition - nearestStep) <= snapTolerance)
        {
            stepPosition = nearestStep;
        }

        return checked((int)Math.Floor(stepPosition));
    }

    private void SaveCheckpoint(int step, float time, int rngCallCount)
    {
        var snapshot = new Particle[_aliveCount];
        Array.Copy(_particles, snapshot, _aliveCount);
        _checkpoints.Add((step, time, snapshot, _aliveCount, rngCallCount));

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
