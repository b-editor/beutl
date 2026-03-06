using System.Runtime.InteropServices;

using Beutl.Graphics.Effects;
using Beutl.Media;

namespace Beutl.Graphics.Particles;

internal sealed class ParticleSimulator
{
    private const float FixedDeltaTime = 1f / 60f;
    private const float CheckpointInterval = 0.5f;
    private const int MaxCheckpoints = 120;

    private readonly PerlinNoise _noise = new();

    private Particle[] _particles = new Particle[256];
    private int _aliveCount;

    // Checkpoint cache
    private readonly List<(float Time, Particle[] Snapshot, int AliveCount, int RngCallCount)> _checkpoints = [];
    private long _parameterVersion;
    private long _lastCachedVersion;

    public void InvalidateCache()
    {
        _parameterVersion++;
    }

    public void Simulate(
        float time,
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

        // Find nearest checkpoint before requested time
        float startTime = 0;
        _aliveCount = 0;
        int rngSkipCount = 0;

        for (int i = _checkpoints.Count - 1; i >= 0; i--)
        {
            if (_checkpoints[i].Time <= time)
            {
                startTime = _checkpoints[i].Time;
                Particle[] snapshot = _checkpoints[i].Snapshot;
                _aliveCount = _checkpoints[i].AliveCount;
                rngSkipCount = _checkpoints[i].RngCallCount;
                EnsureCapacity(snapshot.Length);
                Array.Copy(snapshot, _particles, snapshot.Length);
                // Remove checkpoints after this time (in case of scrub back)
                _checkpoints.RemoveRange(i + 1, _checkpoints.Count - i - 1);
                break;
            }
        }

        // Simulate from startTime to time in fixed steps
        var rng = new CountingRandom(seed, rngSkipCount);

        float currentTime = startTime;
        float nextCheckpoint = startTime + CheckpointInterval;
        if (_checkpoints.Count > 0)
        {
            nextCheckpoint = _checkpoints[^1].Time + CheckpointInterval;
        }

        float dirRad = direction * MathF.PI / 180f;
        float spreadRad = spread * MathF.PI / 180f;

        while (currentTime < time)
        {
            float dt = FixedDeltaTime;
            if (currentTime + dt > time)
            {
                dt = time - currentTime;
            }

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

            currentTime += dt;

            // Save checkpoint
            if (currentTime >= nextCheckpoint)
            {
                SaveCheckpoint(currentTime, rng.CallCount);
                nextCheckpoint = currentTime + CheckpointInterval;
            }
        }
    }

    public ReadOnlyMemory<Particle> GetAliveParticles()
    {
        return _particles.AsMemory(0, _aliveCount);
    }

    private void SaveCheckpoint(float time, int rngCallCount)
    {
        var snapshot = new Particle[_aliveCount];
        Array.Copy(_particles, snapshot, _aliveCount);
        _checkpoints.Add((time, snapshot, _aliveCount, rngCallCount));

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
