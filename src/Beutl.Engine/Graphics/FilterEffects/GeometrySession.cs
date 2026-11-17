using Beutl.Graphics.Rendering;

namespace Beutl.Graphics.Effects;

public sealed class GeometrySession
{
    private readonly RenderCallbackCanvas _canvas;
    private readonly IReadOnlyList<RenderResourceBinding> _resourceBindings;
    private readonly Rect _allocatedOutputBounds;
    private Rect _outputBounds;
    private bool _discarded;

    internal GeometrySession(
        RenderExecutionInput input,
        Rect outputBounds,
        float outputScale,
        float maxWorkingScale,
        RenderIntent intent,
        RenderRequestPurpose purpose,
        RenderCallbackCanvas canvas,
        IReadOnlyList<RenderResourceBinding> resources)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(resources);
        RenderRectValidation.ThrowIfInvalidInput(outputBounds, nameof(outputBounds));
        if (!float.IsFinite(outputScale) || outputScale <= 0)
            throw new ArgumentOutOfRangeException(nameof(outputScale));
        maxWorkingScale = RenderScaleUtilities.SanitizeMaxWorkingScale(maxWorkingScale);

        _canvas = canvas;
        _resourceBindings = resources;
        _allocatedOutputBounds = outputBounds;
        _outputBounds = outputBounds;
        Input = input;
        OutputScale = outputScale;
        MaxWorkingScale = maxWorkingScale;
        Intent = intent;
        Purpose = purpose;
    }

    public RenderExecutionInput Input
    {
        get { _canvas.Token.ThrowIfInactive(); return field; }
    }

    public Rect OutputBounds
    {
        get { _canvas.Token.ThrowIfInactive(); return _outputBounds; }
    }

    /// <summary>The portion of the output allocated for this request.</summary>
    public RenderPlacement OutputPlacement => _canvas.Placement;

    public float OutputScale
    {
        get { _canvas.Token.ThrowIfInactive(); return field; }
    }

    public float MaxWorkingScale
    {
        get { _canvas.Token.ThrowIfInactive(); return field; }
    }

    public RenderIntent Intent
    {
        get { _canvas.Token.ThrowIfInactive(); return field; }
    }

    public RenderRequestPurpose Purpose
    {
        get { _canvas.Token.ThrowIfInactive(); return field; }
    }

    public RenderCallbackCanvas Canvas
    {
        get { _canvas.Token.ThrowIfInactive(); return _canvas; }
    }

    /// <summary>Uses the resource bound to a declared slot.</summary>
    public void UseResource<T>(RenderResourceSlot<T> slot, Action<T> use)
        where T : class
    {
        _canvas.Token.UseResource(slot, _resourceBindings, use);
    }

    public void SetOutputBounds(Rect logicalBounds)
    {
        _canvas.Token.ThrowIfInactive();
        RenderRectValidation.ThrowIfInvalidInput(logicalBounds, nameof(logicalBounds));
        if (!_allocatedOutputBounds.Contains(logicalBounds))
        {
            throw new ArgumentException(
                "Geometry output bounds may only shrink within the allocated output bounds.",
                nameof(logicalBounds));
        }

        _outputBounds = logicalBounds;
    }

    public void DiscardOutput()
    {
        _canvas.Token.ThrowIfInactive();
        _discarded = true;
    }

    internal bool IsOutputDiscarded => _discarded;
}
