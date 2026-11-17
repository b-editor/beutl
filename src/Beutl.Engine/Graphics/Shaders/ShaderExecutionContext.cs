using System.Numerics;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.Graphics.Shaders;

/// <summary>Exposes resolved, stage-local metadata to an execution-time shader binder.</summary>
/// <remarks>
/// The context is valid only during the current compiled shader run's binding phase and must not be retained. Every
/// property throws <see cref="InvalidOperationException"/> after that phase completes.
/// </remarks>
public sealed class ShaderExecutionContext
{
    private readonly RenderExecutionSessionToken _token;
    private readonly Rect _inputBounds;
    private readonly Rect _outputBounds;
    private readonly Rect _requiredRegion;
    private readonly RenderPlacement _outputPlacement;
    private readonly PixelSize _semanticOutputSize;
    private readonly EffectiveScale _inputEffectiveScale;
    private readonly float _outputScale;
    private readonly float _maxWorkingScale;
    private readonly RenderIntent _intent;
    private readonly RenderRequestPurpose _purpose;
    private readonly Point? _inputRasterOrigin;

    internal ShaderExecutionContext(
        RenderExecutionSessionToken token,
        Rect inputBounds,
        Rect outputBounds,
        Rect requiredRegion,
        PixelRect deviceBounds,
        Point logicalOrigin,
        EffectiveScale inputEffectiveScale,
        float outputScale,
        float workingScale,
        float maxWorkingScale,
        RenderIntent intent,
        RenderRequestPurpose purpose,
        Point? inputRasterOrigin = null,
        bool wholeSource = false)
    {
        ArgumentNullException.ThrowIfNull(token);
        _token = token;
        _inputBounds = inputBounds;
        _outputBounds = outputBounds;
        _requiredRegion = requiredRegion;
        _outputPlacement = RenderPlacement.FromRasterOrigin(
            wholeSource ? outputBounds : requiredRegion, EffectiveScale.At(workingScale), deviceBounds, logicalOrigin);
        _semanticOutputSize = _outputPlacement.ToDevice(outputBounds).Size;
        if (_semanticOutputSize.Width <= 0 || _semanticOutputSize.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(outputBounds),
                outputBounds,
                "A shader's semantic output size must be positive.");
        }
        _inputEffectiveScale = inputEffectiveScale;
        _outputScale = outputScale;
        _maxWorkingScale = maxWorkingScale;
        _intent = intent;
        _purpose = purpose;
        _inputRasterOrigin = inputRasterOrigin;
    }

    /// <summary>Gets the stage's complete logical input bounds.</summary>
    /// <exception cref="InvalidOperationException">The shader binding phase has completed.</exception>
    public Rect InputBounds
    {
        get { _token.ThrowIfInactive(); return _inputBounds; }
    }

    /// <summary>Gets the stage's complete logical output bounds.</summary>
    /// <exception cref="InvalidOperationException">The shader binding phase has completed.</exception>
    public Rect OutputBounds
    {
        get { _token.ThrowIfInactive(); return _outputBounds; }
    }

    /// <summary>Gets the stage-local logical output region required by the current request.</summary>
    /// <exception cref="InvalidOperationException">The shader binding phase has completed.</exception>
    public Rect RequiredRegion
    {
        get { _token.ThrowIfInactive(); return _requiredRegion; }
    }

    /// <summary>The evaluated region, or the complete output for a whole-source stage.</summary>
    public RenderPlacement OutputPlacement
    {
        get { _token.ThrowIfInactive(); return _outputPlacement; }
    }

    /// <summary>Gets the complete semantic output dimensions in working-density pixels.</summary>
    /// <remarks>
    /// This covers the full output even when a current-pixel stage evaluates only a requested region.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The shader binding phase has completed.</exception>
    public PixelSize SemanticOutputSize
    {
        get { _token.ThrowIfInactive(); return _semanticOutputSize; }
    }

    // Where the texel grid of a whole-source stage's src starts, in local logical coordinates; null for other stages.
    internal Point? InputRasterOrigin
    {
        get { _token.ThrowIfInactive(); return _inputRasterOrigin; }
    }

    /// <summary>Gets the effective-scale supply resolved for the stage input.</summary>
    /// <remarks>
    /// The first fused stage receives the materialized input scale; later stages receive the fused run's
    /// <see cref="RenderPlacement.Scale"/>.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The shader binding phase has completed.</exception>
    public EffectiveScale InputEffectiveScale
    {
        get { _token.ThrowIfInactive(); return _inputEffectiveScale; }
    }

    /// <summary>Gets the final output density requested for the render, in device pixels per logical unit.</summary>
    /// <remarks>This value is not an intermediate allocation ceiling; use <see cref="RenderPlacement.Scale"/> for execution.</remarks>
    /// <exception cref="InvalidOperationException">The shader binding phase has completed.</exception>
    public float OutputScale
    {
        get { _token.ThrowIfInactive(); return _outputScale; }
    }

    /// <summary>Gets the sanitized maximum working density allowed by the render request.</summary>
    /// <exception cref="InvalidOperationException">The shader binding phase has completed.</exception>
    public float MaxWorkingScale
    {
        get { _token.ThrowIfInactive(); return _maxWorkingScale; }
    }

    /// <summary>Gets whether the request targets interactive preview or delivery-quality rendering.</summary>
    /// <exception cref="InvalidOperationException">The shader binding phase has completed.</exception>
    public RenderIntent Intent
    {
        get { _token.ThrowIfInactive(); return _intent; }
    }

    /// <summary>Gets the high-level operation that caused this render request.</summary>
    /// <exception cref="InvalidOperationException">The shader binding phase has completed.</exception>
    public RenderRequestPurpose Purpose
    {
        get { _token.ThrowIfInactive(); return _purpose; }
    }
}
