using SkiaSharp;

namespace Beutl.Graphics.AudioVisualizers;

/// <summary>
/// Holds the <see cref="SKPathEffect"/> that rounds a polyline's corners, rebuilding it only when the
/// radius it was built for moves.
/// </summary>
/// <remarks>
/// <see cref="SKPathEffect.CreateCorner"/> allocates a native object, and a shape asks for the same
/// radius on every frame its smoothness is not animating, so the effect has to outlive the frame that
/// created it.
/// </remarks>
internal sealed class CornerPathEffectCache : IDisposable
{
    private float _radius = -1f;
    private SKPathEffect? _effect;

    /// <summary>
    /// Gets the corner effect for <paramref name="radius"/>, or <see langword="null"/> when the radius is
    /// too small to round anything.
    /// </summary>
    /// <remarks>
    /// The effect stays owned by this cache, so the caller assigns it to a paint but never disposes it.
    /// </remarks>
    public SKPathEffect? GetOrCreate(float radius)
    {
        if (_radius != radius)
        {
            _effect?.Dispose();
            _effect = radius > 0.01f ? SKPathEffect.CreateCorner(radius) : null;
            _radius = radius;
        }

        return _effect;
    }

    public void Dispose()
    {
        _effect?.Dispose();
        _effect = null;
        _radius = -1f;
    }
}
