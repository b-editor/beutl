using Beutl.Graphics.Rendering.Requests;
using Beutl.Media;

namespace Beutl.Graphics.Rendering;

public sealed class OpaqueRenderSession
{
    private readonly RenderExecutionSessionToken _token;
    private readonly IReadOnlyList<RenderResourceBinding> _resourceBindings;
    private readonly Func<OpaqueRenderSession, Rect, float?, OpaqueRenderOutput> _createOutput;
    private readonly Action<OpaqueRenderOutput> _publish;
    private readonly IReadOnlyList<RenderExecutionInput> _inputs;
    private readonly IReadOnlyList<RenderExecutionInputRange> _inputRanges;
    private readonly Rect _outputBounds;
    private readonly Rect _requiredRegion;
    private readonly PixelRect _deviceBounds;
    private readonly float _outputScale;
    private readonly float _workingScale;
    private readonly float _maxWorkingScale;
    private readonly RenderIntent _intent;
    private readonly RenderRequestPurpose _purpose;

    internal OpaqueRenderSession(
        RenderExecutionSessionToken token,
        IReadOnlyList<RenderExecutionInput> inputs,
        IReadOnlyList<RenderExecutionInputRange> inputRanges,
        Rect outputBounds,
        Rect requiredRegion,
        PixelRect deviceBounds,
        float outputScale,
        float workingScale,
        float maxWorkingScale,
        RenderIntent intent,
        RenderRequestPurpose purpose,
        IReadOnlyList<RenderResourceBinding> resources,
        Func<OpaqueRenderSession, Rect, float?, OpaqueRenderOutput> createOutput,
        Action<OpaqueRenderOutput> publish)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(inputRanges);
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(createOutput);
        ArgumentNullException.ThrowIfNull(publish);
        _token = token;
        _inputs = inputs.Count == 0
            ? Array.Empty<RenderExecutionInput>()
            : Array.AsReadOnly(inputs.ToArray());
        _inputRanges = RenderExecutionInputRange.CopyAndValidate(
            _inputs,
            inputRanges,
            nameof(inputRanges));
        _outputBounds = outputBounds;
        _requiredRegion = requiredRegion;
        _deviceBounds = deviceBounds;
        _outputScale = outputScale;
        _workingScale = workingScale;
        _maxWorkingScale = maxWorkingScale;
        _intent = intent;
        _purpose = purpose;
        _resourceBindings = resources;
        _createOutput = createOutput;
        _publish = publish;
    }

    internal RenderExecutionSessionToken Token => _token;

    public IReadOnlyList<RenderExecutionInput> Inputs
    {
        get { _token.ThrowIfInactive(); return _inputs; }
    }

    /// <summary>
    /// Gets one stable flattened-input range per authored input handle, including zero-length ranges for handles
    /// that produced no runtime values.
    /// </summary>
    public IReadOnlyList<RenderExecutionInputRange> InputRanges
    {
        get { _token.ThrowIfInactive(); return _inputRanges; }
    }

    public Rect OutputBounds
    {
        get { _token.ThrowIfInactive(); return _outputBounds; }
    }

    public Rect RequiredRegion
    {
        get { _token.ThrowIfInactive(); return _requiredRegion; }
    }

    public PixelRect DeviceBounds
    {
        get { _token.ThrowIfInactive(); return _deviceBounds; }
    }

    public PixelSize DeviceSize
    {
        get { _token.ThrowIfInactive(); return _deviceBounds.Size; }
    }

    public float OutputScale
    {
        get { _token.ThrowIfInactive(); return _outputScale; }
    }

    public float WorkingScale
    {
        get { _token.ThrowIfInactive(); return _workingScale; }
    }

    public float MaxWorkingScale
    {
        get { _token.ThrowIfInactive(); return _maxWorkingScale; }
    }

    public RenderIntent Intent
    {
        get { _token.ThrowIfInactive(); return _intent; }
    }

    public RenderRequestPurpose Purpose
    {
        get { _token.ThrowIfInactive(); return _purpose; }
    }

    /// <summary>Creates an unpublished output within the declared bounds.</summary>
    /// <param name="logicalBounds">The finite non-empty logical output bounds.</param>
    /// <param name="density">
    /// The optional finite positive density for this output. <see langword="null"/> uses
    /// <see cref="WorkingScale"/>. The executor clamps either value to engine allocation limits.
    /// </param>
    public OpaqueRenderOutput CreateOutput(Rect logicalBounds, float? density = null)
    {
        _token.ThrowIfInactive();
        RenderDescriptionValidation.ThrowIfFiniteNonEmpty(logicalBounds, nameof(logicalBounds));
        if (density is { } value && (!float.IsFinite(value) || value <= 0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(density),
                density,
                "An opaque output density must be finite and positive.");
        }
        if (!_outputBounds.Contains(logicalBounds))
        {
            throw new ArgumentException("An opaque output must be contained by the declared output bounds.", nameof(logicalBounds));
        }

        return _createOutput(this, logicalBounds, density);
    }

    public void Publish(OpaqueRenderOutput output)
    {
        _token.ThrowIfInactive();
        ArgumentNullException.ThrowIfNull(output);
        output.Publish(this, _publish);
    }

    /// <summary>Uses the resource bound to a declared slot.</summary>
    public void UseResource<T>(RenderResourceSlot<T> slot, Action<T> use)
        where T : class
    {
        _token.UseResource(slot, _resourceBindings, use);
    }

    internal void UseResource<T>(RenderResource<T> resource, Action<T> use)
        where T : class
    {
        _token.UseResource(resource, _resourceBindings, use);
    }

    internal void UseNestedTarget(
        RenderResource<NestedRenderTargetBinding> resource,
        Action<NestedRenderTargetImage> use)
    {
        ArgumentNullException.ThrowIfNull(use);
        _token.UseResource(
            resource,
            _resourceBindings,
            binding => binding.UseImage(_token, use));
    }
}
