using Beutl.Media;

namespace Beutl.Graphics.Rendering;

public sealed class TargetCommandSession
{
    private readonly IReadOnlyList<RenderExecutionInput> _inputs;
    private readonly IReadOnlyList<RenderExecutionInputRange> _inputRanges;
    private readonly Rect _affectedBounds;
    private readonly RenderIntent _intent;
    private readonly RenderRequestPurpose _purpose;
    private readonly RenderCallbackCanvas _canvas;
    private readonly IReadOnlyList<RenderResourceBinding> _resourceBindings;
    private readonly Func<Bitmap>? _createSnapshot;
    private bool _snapshotUsed;

    internal TargetCommandSession(
        IReadOnlyList<RenderExecutionInput> inputs,
        IReadOnlyList<RenderExecutionInputRange> inputRanges,
        Rect affectedBounds,
        RenderIntent intent,
        RenderRequestPurpose purpose,
        RenderCallbackCanvas canvas,
        IReadOnlyList<RenderResourceBinding> resources,
        Func<Bitmap>? createSnapshot)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(inputRanges);
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(resources);
        _inputs = inputs.Count == 0
            ? Array.Empty<RenderExecutionInput>()
            : Array.AsReadOnly(inputs.ToArray());
        _inputRanges = RenderExecutionInputRange.CopyAndValidate(
            _inputs,
            inputRanges,
            nameof(inputRanges));
        _affectedBounds = affectedBounds;
        _intent = intent;
        _purpose = purpose;
        _canvas = canvas;
        _resourceBindings = resources;
        _createSnapshot = createSnapshot;
    }

    public IReadOnlyList<RenderExecutionInput> Inputs
    {
        get { _canvas.Token.ThrowIfInactive(); return _inputs; }
    }

    /// <summary>
    /// Gets one stable flattened-input range per authored input handle, including zero-length ranges for handles
    /// that produced no runtime values.
    /// </summary>
    public IReadOnlyList<RenderExecutionInputRange> InputRanges
    {
        get { _canvas.Token.ThrowIfInactive(); return _inputRanges; }
    }

    public Rect AffectedBounds
    {
        get { _canvas.Token.ThrowIfInactive(); return _affectedBounds; }
    }

    public Rect RequiredRegion => _canvas.LogicalBounds;

    public RenderIntent Intent
    {
        get { _canvas.Token.ThrowIfInactive(); return _intent; }
    }

    public RenderRequestPurpose Purpose
    {
        get { _canvas.Token.ThrowIfInactive(); return _purpose; }
    }

    public RenderCallbackCanvas Canvas
    {
        get { _canvas.Token.ThrowIfInactive(); return _canvas; }
    }

    /// <summary>Replaces every pixel in the declared affected region with <paramref name="color"/>.</summary>
    /// <remarks>
    /// The operation uses clipped source replacement, so a transparent color erases a finite region without
    /// exposing unrestricted blend state or writing outside the command's declared target access.
    /// </remarks>
    public void ReplaceAffectedRegion(Color color)
    {
        _canvas.Token.ThrowIfInactive();
        _canvas.Use(canvas => canvas.ReplaceAffectedRegion(color));
    }

    public void UseSnapshot(Action<Bitmap> use)
    {
        _canvas.Token.ThrowIfInactive();
        ArgumentNullException.ThrowIfNull(use);
        if (_createSnapshot is null)
            throw new InvalidOperationException("This target command did not declare target readback.");
        if (_snapshotUsed)
            throw new InvalidOperationException("The target snapshot is a one-shot execution lease.");

        _snapshotUsed = true;
        using Bitmap snapshot = _createSnapshot()
            ?? throw new InvalidOperationException("The target snapshot provider returned null.");
        _canvas.Token.AuthorizeResource(snapshot, () => use(snapshot));
    }

    /// <summary>Uses the resource bound to a declared slot.</summary>
    public void UseResource<T>(RenderResourceSlot<T> slot, Action<T> use)
        where T : class
    {
        _canvas.Token.UseResource(slot, _resourceBindings, use);
    }

    internal void ValidateCompletion()
    {
        _canvas.Token.ThrowIfInactive();
        if (_createSnapshot is not null && !_snapshotUsed)
            throw new InvalidOperationException("A readback target command must consume its snapshot exactly once.");
    }
}
