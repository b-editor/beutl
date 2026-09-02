using Beutl.Media;

namespace Beutl.Graphics.Rendering;

public sealed class TargetCommandSession
{
    private readonly RenderExecutionSessionToken _token;
    private readonly IReadOnlyList<RenderExecutionInput> _inputs;
    private readonly IReadOnlyList<RenderExecutionInputRange> _inputRanges;
    private readonly Rect _affectedBounds;
    private readonly Rect _requiredRegion;
    private readonly RenderIntent _intent;
    private readonly RenderRequestPurpose _purpose;
    private readonly RenderCallbackCanvas _canvas;
    private readonly IReadOnlyList<RenderResourceBinding> _resourceBindings;
    private readonly IReadOnlyList<RenderResource> _resources;
    private readonly Func<Bitmap>? _createSnapshot;
    private readonly bool _snapshotRequired;
    private bool _snapshotUsed;

    internal TargetCommandSession(
        RenderExecutionSessionToken token,
        IReadOnlyList<RenderExecutionInput> inputs,
        IReadOnlyList<RenderExecutionInputRange> inputRanges,
        Rect affectedBounds,
        Rect requiredRegion,
        RenderIntent intent,
        RenderRequestPurpose purpose,
        RenderCallbackCanvas canvas,
        IReadOnlyList<RenderResourceBinding> resources,
        bool snapshotRequired,
        Func<Bitmap>? createSnapshot)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(inputRanges);
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(resources);
        _token = token;
        _inputs = Array.AsReadOnly(inputs.ToArray());
        _inputRanges = RenderExecutionInputRange.CopyAndValidate(
            _inputs,
            inputRanges,
            nameof(inputRanges));
        _affectedBounds = affectedBounds;
        _requiredRegion = requiredRegion;
        _intent = intent;
        _purpose = purpose;
        _canvas = canvas;
        _resourceBindings = resources;
        _resources = resources.SelectToArray(static binding => binding.Resource);
        _snapshotRequired = snapshotRequired;
        _createSnapshot = createSnapshot;
    }

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

    public Rect AffectedBounds
    {
        get { _token.ThrowIfInactive(); return _affectedBounds; }
    }

    public Rect RequiredRegion
    {
        get { _token.ThrowIfInactive(); return _requiredRegion; }
    }

    public RenderIntent Intent
    {
        get { _token.ThrowIfInactive(); return _intent; }
    }

    public RenderRequestPurpose Purpose
    {
        get { _token.ThrowIfInactive(); return _purpose; }
    }

    public RenderCallbackCanvas Canvas
    {
        get { _token.ThrowIfInactive(); return _canvas; }
    }

    /// <summary>Replaces every pixel in the declared affected region with <paramref name="color"/>.</summary>
    /// <remarks>
    /// The operation uses clipped source replacement, so a transparent color erases a finite region without
    /// exposing unrestricted blend state or writing outside the command's declared target access.
    /// </remarks>
    public void ReplaceAffectedRegion(Color color)
    {
        _token.ThrowIfInactive();
        _canvas.Use(canvas => canvas.ReplaceAffectedRegion(color));
    }

    public void UseSnapshot(Action<Bitmap> use)
    {
        _token.ThrowIfInactive();
        ArgumentNullException.ThrowIfNull(use);
        if (!_snapshotRequired || _createSnapshot is null)
            throw new InvalidOperationException("This target command did not declare target readback.");
        if (_snapshotUsed)
            throw new InvalidOperationException("The target snapshot is a one-shot execution lease.");

        _snapshotUsed = true;
        using Bitmap snapshot = _createSnapshot()
            ?? throw new InvalidOperationException("The target snapshot provider returned null.");
        _token.AuthorizeResource(snapshot, () => use(snapshot));
    }

    /// <summary>Uses the resource bound to a declared slot.</summary>
    public void UseResource<T>(RenderResourceSlot<T> slot, Action<T> use)
        where T : class
    {
        _token.UseResource(slot, _resourceBindings, use);
    }

    internal void ValidateCompletion()
    {
        _token.ThrowIfInactive();
        if (_snapshotRequired && !_snapshotUsed)
            throw new InvalidOperationException("A readback target command must consume its snapshot exactly once.");
    }
}
