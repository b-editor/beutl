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
    private readonly PixelRect _deviceBounds;
    private readonly PixelSize _semanticOutputSize;
    private readonly Point _logicalOrigin;
    private readonly EffectiveScale _inputEffectiveScale;
    private readonly float _outputScale;
    private readonly float _workingScale;
    private readonly float _maxWorkingScale;
    private readonly RenderIntent _intent;
    private readonly RenderRequestPurpose _purpose;

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
        RenderRequestPurpose purpose)
    {
        ArgumentNullException.ThrowIfNull(token);
        _token = token;
        _inputBounds = inputBounds;
        _outputBounds = outputBounds;
        _requiredRegion = requiredRegion;
        _deviceBounds = deviceBounds;
        _logicalOrigin = logicalOrigin;
        var deviceGridOffset = new Vector(
            (deviceBounds.X / workingScale) - logicalOrigin.X,
            (deviceBounds.Y / workingScale) - logicalOrigin.Y);
        _semanticOutputSize = PixelRect.FromRect(
                outputBounds.Translate(deviceGridOffset),
                workingScale)
            .Size;
        if (_semanticOutputSize.Width <= 0 || _semanticOutputSize.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(outputBounds),
                outputBounds,
                "A shader's semantic output size must be positive.");
        }
        _inputEffectiveScale = inputEffectiveScale;
        _outputScale = outputScale;
        _workingScale = workingScale;
        _maxWorkingScale = maxWorkingScale;
        _intent = intent;
        _purpose = purpose;
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

    /// <summary>Gets the footprint the stage is evaluated over, in composition-device pixels.</summary>
    /// <remarks>
    /// The footprint reflects the actual runtime-clamped <see cref="WorkingScale"/>.
    /// Subtract <see cref="DeviceGridOffset"/> after converting it to logical units to obtain
    /// the stage-local footprint.
    /// A <see cref="ShaderDescriptionKind.CurrentPixel"/> stage is evaluated over the region the request asked
    /// for, so this is its destination footprint. A <see cref="ShaderDescriptionKind.WholeSource"/> stage is
    /// evaluated over its complete output regardless of how much of it was requested, so this is the complete
    /// output footprint and its size equals <see cref="SemanticOutputSize"/>; use <see cref="RequiredRegion"/>
    /// for the part actually being produced.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The shader binding phase has completed.</exception>
    public PixelRect DeviceBounds
    {
        get { _token.ThrowIfInactive(); return _deviceBounds; }
    }

    /// <summary>Gets the evaluated footprint size, equal to <see cref="DeviceBounds"/>.<c>Size</c>.</summary>
    /// <exception cref="InvalidOperationException">The shader binding phase has completed.</exception>
    public PixelSize DeviceSize
    {
        get { _token.ThrowIfInactive(); return _deviceBounds.Size; }
    }

    /// <summary>Gets the complete semantic output dimensions in working-density pixels.</summary>
    /// <remarks>
    /// The size is derived from <see cref="OutputBounds"/>, <see cref="WorkingScale"/>, and
    /// <see cref="DeviceGridOffset"/>. It is independent of the physical backing selected by the execution planner.
    /// It matches <see cref="DeviceSize"/> for a <see cref="ShaderDescriptionKind.WholeSource"/> stage and
    /// describes the complete output rather than the requested region for a
    /// <see cref="ShaderDescriptionKind.CurrentPixel"/> one.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The shader binding phase has completed.</exception>
    public PixelSize SemanticOutputSize
    {
        get { _token.ThrowIfInactive(); return _semanticOutputSize; }
    }

    /// <summary>
    /// Gets the translation from stage-local coordinates to the composition-device grid used to
    /// round <see cref="DeviceBounds"/>.
    /// </summary>
    public Vector DeviceGridOffset
    {
        get
        {
            _token.ThrowIfInactive();
            return new Vector(
                (_deviceBounds.X / _workingScale) - _logicalOrigin.X,
                (_deviceBounds.Y / _workingScale) - _logicalOrigin.Y);
        }
    }

    /// <summary>Gets the logical point represented by local output-device coordinate <c>(0, 0)</c>.</summary>
    /// <remarks>
    /// A local device coordinate <c>coord</c> represents
    /// <c>LogicalOrigin + coord / WorkingScale</c>. The origin follows <see cref="DeviceBounds"/>, so a
    /// <see cref="ShaderDescriptionKind.WholeSource"/> stage's <c>coord</c> spans
    /// <c>[0, SemanticOutputSize]</c> over its complete output even when a smaller region was requested.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The shader binding phase has completed.</exception>
    public Point LogicalOrigin
    {
        get
        {
            _token.ThrowIfInactive();
            return _logicalOrigin;
        }
    }

    /// <summary>Gets the effective-scale supply resolved for the stage input.</summary>
    /// <remarks>
    /// The first fused stage receives the materialized input scale; later stages receive the fused run's
    /// <see cref="WorkingScale"/>.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The shader binding phase has completed.</exception>
    public EffectiveScale InputEffectiveScale
    {
        get { _token.ThrowIfInactive(); return _inputEffectiveScale; }
    }

    /// <summary>Gets the final output density requested for the render, in device pixels per logical unit.</summary>
    /// <remarks>This value is not an intermediate allocation ceiling; use <see cref="WorkingScale"/> for execution.</remarks>
    /// <exception cref="InvalidOperationException">The shader binding phase has completed.</exception>
    public float OutputScale
    {
        get { _token.ThrowIfInactive(); return _outputScale; }
    }

    /// <summary>
    /// Gets the positive finite density selected for this stage after working-scale and allocation-limit clamping.
    /// </summary>
    /// <exception cref="InvalidOperationException">The shader binding phase has completed.</exception>
    public float WorkingScale
    {
        get { _token.ThrowIfInactive(); return _workingScale; }
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
