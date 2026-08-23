namespace Beutl.Graphics.Rendering;

/// <summary>
/// The request values a node needs before its children are recorded.
/// </summary>
/// <remarks>
/// Recording walks children before their parent, so a node that owns scale-dependent children has no way to
/// rebuild them from <see cref="RenderNode.Process"/> - by then they have already been recorded. This is what
/// it gets first, and it carries only what is settled before any fragment exists: the request's own values.
/// </remarks>
public readonly struct RenderNodePreparation
{
    internal RenderNodePreparation(RenderRequestOptions options)
    {
        OutputScale = options.OutputScale;
        MaxWorkingScale = options.MaxWorkingScale;
        Intent = options.Intent;
        Purpose = options.Purpose;
        TargetDomain = options.TargetDomain;
    }

    /// <summary>Gets the density of the final target this request delivers to.</summary>
    public float OutputScale { get; }

    /// <summary>Gets the ceiling on any working density this request resolves.</summary>
    public float MaxWorkingScale { get; }

    /// <summary>Gets what this request's output is for.</summary>
    public RenderIntent Intent { get; }

    /// <summary>Gets the kind of answer this request asks for.</summary>
    public RenderRequestPurpose Purpose { get; }

    /// <summary>Gets the region this request's output is clipped to, when it has one.</summary>
    public Rect? TargetDomain { get; }
}
