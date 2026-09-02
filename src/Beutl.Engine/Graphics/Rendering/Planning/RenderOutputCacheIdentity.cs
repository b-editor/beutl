namespace Beutl.Graphics.Rendering;

/// <summary>
/// Complete runtime identity for one materialized render-cache value. The hash is a bucket hint only;
/// <see cref="Equals(RenderOutputCacheIdentity?)"/> compares every retained component.
/// </summary>
internal sealed class RenderOutputCacheIdentity : IEquatable<RenderOutputCacheIdentity>
{
    private readonly object _candidateKey;
    private readonly RenderFragmentOutputIdentity _fragment;
    private readonly Rect _bounds;
    private readonly RequiredRegion _coverage;
    private readonly int _densityBits;
    private readonly RenderCacheFormatIdentity _format;
    private readonly RenderIntent _intent;
    private readonly RenderRequestPurpose _purpose;
    private readonly FusionMode _fusionMode;
    private readonly RenderCacheDeviceContextIdentity _deviceContext;
    private readonly Vector _deviceGridOffset;

    public RenderOutputCacheIdentity(
        object candidateKey,
        RenderFragmentOutputIdentity fragment,
        Rect bounds,
        RequiredRegion coverage,
        float density,
        RenderCacheFormatIdentity format,
        RenderIntent intent,
        RenderRequestPurpose purpose,
        FusionMode fusionMode,
        RenderCacheDeviceContextIdentity deviceContext,
        Vector deviceGridOffset = default)
    {
        ArgumentNullException.ThrowIfNull(candidateKey);
        ArgumentNullException.ThrowIfNull(fragment);
        if (!RenderRectValidation.IsFiniteNonNegative(bounds))
            throw new ArgumentException("Cache bounds must be finite and non-negative.", nameof(bounds));
        if (!float.IsFinite(density) || density <= 0)
            throw new ArgumentOutOfRangeException(nameof(density), density, "Cache density must be finite and positive.");
        format.ThrowIfUninitialized(nameof(format));
        deviceContext.ThrowIfUninitialized(nameof(deviceContext));
        if (!Enum.IsDefined(intent))
            throw new ArgumentOutOfRangeException(nameof(intent));
        if (!Enum.IsDefined(purpose))
            throw new ArgumentOutOfRangeException(nameof(purpose));
        if (!Enum.IsDefined(fusionMode))
            throw new ArgumentOutOfRangeException(nameof(fusionMode));

        _candidateKey = candidateKey;
        _fragment = fragment;
        _bounds = bounds;
        _coverage = coverage;
        _densityBits = BitConverter.SingleToInt32Bits(density);
        _format = format;
        _intent = intent;
        _purpose = purpose;
        _fusionMode = fusionMode;
        _deviceContext = deviceContext;
        _deviceGridOffset = deviceGridOffset;
    }

    public object CandidateKey => _candidateKey;

    public Rect Bounds => _bounds;

    public RequiredRegion Coverage => _coverage;

    public float Density => BitConverter.Int32BitsToSingle(_densityBits);

    public RenderCacheFormatIdentity Format => _format;

    public RenderIntent Intent => _intent;

    public RenderRequestPurpose Purpose => _purpose;

    public FusionMode FusionMode => _fusionMode;

    public RenderCacheDeviceContextIdentity DeviceContext => _deviceContext;

    public Vector DeviceGridOffset => _deviceGridOffset;

    public bool Equals(RenderOutputCacheIdentity? other)
        => other is not null
           && Equals(_candidateKey, other._candidateKey)
           && _fragment.Equals(other._fragment)
           && _bounds.Equals(other._bounds)
           && _coverage.Equals(other._coverage)
           && _densityBits == other._densityBits
           && _format.Equals(other._format)
           && _intent == other._intent
           && _purpose == other._purpose
           && _fusionMode == other._fusionMode
           && _deviceContext.Equals(other._deviceContext)
           && _deviceGridOffset.Equals(other._deviceGridOffset);

    public override bool Equals(object? obj)
        => obj is RenderOutputCacheIdentity other && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine(
            _candidateKey,
            _fragment,
            _bounds,
            _coverage,
            _densityBits,
            _format,
            HashCode.Combine(_intent, _purpose, _fusionMode, _deviceContext, _deviceGridOffset));
}
