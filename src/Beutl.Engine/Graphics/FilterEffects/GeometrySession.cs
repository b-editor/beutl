using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.Graphics.Effects;

public sealed class GeometrySession
{
    private readonly RenderExecutionSessionToken _token;
    private readonly IReadOnlyList<RenderResource> _resources;
    private readonly Rect _allocatedOutputBounds;
    private Rect _outputBounds;
    private bool _discarded;

    internal GeometrySession(
        RenderExecutionSessionToken token,
        RenderExecutionInput input,
        Rect outputBounds,
        Rect requiredRegion,
        PixelRect deviceBounds,
        float outputScale,
        float workingScale,
        float maxWorkingScale,
        RenderIntent intent,
        RenderRequestPurpose purpose,
        RenderCallbackCanvas canvas,
        IReadOnlyList<RenderResource> resources)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(resources);
        RenderRectValidation.ThrowIfInvalidInput(outputBounds, nameof(outputBounds));
        RenderRectValidation.ThrowIfInvalidInput(requiredRegion, nameof(requiredRegion));
        if (!float.IsFinite(outputScale) || outputScale <= 0)
            throw new ArgumentOutOfRangeException(nameof(outputScale));
        if (!float.IsFinite(workingScale) || workingScale <= 0)
            throw new ArgumentOutOfRangeException(nameof(workingScale));
        maxWorkingScale = RenderScaleUtilities.SanitizeMaxWorkingScale(maxWorkingScale);

        _token = token;
        _resources = resources;
        _allocatedOutputBounds = outputBounds;
        _outputBounds = outputBounds;
        Input = input;
        RequiredRegion = requiredRegion;
        DeviceBounds = deviceBounds;
        OutputScale = outputScale;
        WorkingScale = workingScale;
        MaxWorkingScale = maxWorkingScale;
        Intent = intent;
        Purpose = purpose;
        Canvas = canvas;
    }

    public RenderExecutionInput Input
    {
        get { _token.ThrowIfInactive(); return field; }
    }

    public Rect OutputBounds
    {
        get { _token.ThrowIfInactive(); return _outputBounds; }
    }

    public Rect RequiredRegion
    {
        get { _token.ThrowIfInactive(); return field; }
    }

    public PixelRect DeviceBounds
    {
        get { _token.ThrowIfInactive(); return field; }
    }

    public PixelSize DeviceSize
    {
        get { _token.ThrowIfInactive(); return DeviceBounds.Size; }
    }

    public float OutputScale
    {
        get { _token.ThrowIfInactive(); return field; }
    }

    public float WorkingScale
    {
        get { _token.ThrowIfInactive(); return field; }
    }

    public float MaxWorkingScale
    {
        get { _token.ThrowIfInactive(); return field; }
    }

    public RenderIntent Intent
    {
        get { _token.ThrowIfInactive(); return field; }
    }

    public RenderRequestPurpose Purpose
    {
        get { _token.ThrowIfInactive(); return field; }
    }

    public RenderCallbackCanvas Canvas
    {
        get { _token.ThrowIfInactive(); return field; }
    }

    public void UseResource<T>(RenderResource<T> resource, Action<T> use)
        where T : class
    {
        _token.UseResource(resource, _resources, use);
    }

    public void SetOutputBounds(Rect logicalBounds)
    {
        _token.ThrowIfInactive();
        RenderRectValidation.ThrowIfInvalidInput(logicalBounds, nameof(logicalBounds));
        if (!RenderDescriptionValidation.Contains(_allocatedOutputBounds, logicalBounds))
        {
            throw new ArgumentException(
                "Geometry output bounds may only shrink within the allocated output bounds.",
                nameof(logicalBounds));
        }

        _outputBounds = logicalBounds;
    }

    public void DiscardOutput()
    {
        _token.ThrowIfInactive();
        _discarded = true;
    }

    internal bool IsOutputDiscarded => _discarded;
}
