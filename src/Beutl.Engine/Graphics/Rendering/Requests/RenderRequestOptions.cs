using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering.Cache;

namespace Beutl.Graphics.Rendering.Requests;

internal sealed class RenderRequestOptions
{
    public RenderRequestOptions(
        RenderIntent intent,
        RenderRequestPurpose purpose,
        Rect? targetDomain = null,
        Rect? requestedRegion = null,
        float outputScale = 1,
        float maxWorkingScale = float.PositiveInfinity,
        RenderCacheOptions? cachePolicy = null,
        FusionMode fusionMode = FusionMode.Enabled,
        RenderRequestOwner? owner = null,
        NestedRenderTargetBinding? targetBinding = null,
        bool supports3DRendering = true,
        Device3DExtentBudget device3DExtentBudget = default,
        bool suppressGizmos = false)
    {
        if (!Enum.IsDefined(intent))
        {
            throw new ArgumentOutOfRangeException(nameof(intent), intent, "The render intent is not defined.");
        }

        if (!Enum.IsDefined(purpose))
        {
            throw new ArgumentOutOfRangeException(nameof(purpose), purpose, "The render request purpose is not defined.");
        }

        if (!Enum.IsDefined(fusionMode))
        {
            throw new ArgumentOutOfRangeException(nameof(fusionMode), fusionMode, "The fusion mode is not defined.");
        }

        ValidateTargetDomain(targetDomain);
        ValidateRequestedRegion(requestedRegion);

        Intent = intent;
        Purpose = purpose;
        TargetDomain = targetDomain;
        RequestedRegion = requestedRegion;
        OutputScale = RenderScaleUtilities.SanitizeOutputScale(outputScale);
        MaxWorkingScale = RenderScaleUtilities.SanitizeMaxWorkingScale(maxWorkingScale);
        RenderCacheOptions sourceCachePolicy = cachePolicy ?? RenderCacheOptions.Default;
        CachePolicy = new RenderCacheOptions(sourceCachePolicy.IsEnabled, sourceCachePolicy.Rules);
        FusionMode = fusionMode;
        Owner = owner ?? new RenderRequestOwner();
        OwnsOwner = owner is null;
        TargetBinding = targetBinding;
        Supports3DRendering = supports3DRendering;
        Device3DExtentBudget = device3DExtentBudget;
        SuppressGizmos = suppressGizmos;
        PlanIdentity = new RenderRequestPlanIdentity(
            Purpose,
            FusionMode,
            CachePolicy.IsEnabled,
            CachePolicy.Rules);
    }

    public RenderIntent Intent { get; }

    public bool SuppressGizmos { get; }

    public RenderRequestPurpose Purpose { get; }

    public Rect? TargetDomain { get; }

    public Rect? RequestedRegion { get; }

    public float OutputScale { get; }

    public float MaxWorkingScale { get; }

    public RenderCacheOptions CachePolicy { get; }

    public FusionMode FusionMode { get; }

    public RenderRequestOwner Owner { get; }

    public NestedRenderTargetBinding? TargetBinding { get; }

    public RenderRequestPlanIdentity PlanIdentity { get; }

    /// <summary>Whether this request may expect a 3D-capable backend at execution.</summary>
    /// <remarks>
    /// Settled at creation; a nested request inherits it. The entry renderer predicts it, and a request built
    /// directly assumes <see langword="true"/> before any backend exists.
    /// </remarks>
    public bool Supports3DRendering { get; }

    /// <summary>The 3D extents this request may expect to allocate, or unreported when unknown.</summary>
    /// <remarks>
    /// Carried like <see cref="Supports3DRendering"/>, so a node can ask at record time whether its 3D extents can be
    /// made. <see cref="Device3DExtentBudget.Unreported"/> refuses nothing; the allocation still decides.
    /// </remarks>
    public Device3DExtentBudget Device3DExtentBudget { get; }

    internal bool OwnsOwner { get; }

    internal RenderRequestOptions? NestedPolicyParent { get; private set; }

    public RenderRequestOptions CreateNestedAtScale(
        NestedRenderTargetBinding targetBinding,
        float workingScale,
        Rect? targetDomain = null,
        Rect? requestedRegion = null)
    {
        if (!float.IsFinite(workingScale) || workingScale <= 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(workingScale),
                workingScale,
                "A nested target working scale must be positive and finite.");
        }

        ArgumentNullException.ThrowIfNull(targetBinding);
        var nested = new RenderRequestOptions(
            Intent,
            Purpose,
            targetDomain ?? TargetDomain,
            requestedRegion,
            workingScale,
            workingScale,
            CachePolicy,
            FusionMode,
            Owner,
            targetBinding,
            Supports3DRendering,
            Device3DExtentBudget,
            SuppressGizmos);
        nested.NestedPolicyParent = this;
        return nested;
    }

    private static void ValidateTargetDomain(Rect? targetDomain)
    {
        if (targetDomain is not { } value)
        {
            return;
        }

        if (!RenderRectValidation.IsFiniteNonNegative(value)
            || value.Width <= 0
            || value.Height <= 0)
        {
            throw new ArgumentException(
                "A target domain must be a finite rectangle with positive width and height.",
                nameof(targetDomain));
        }
    }

    private static void ValidateRequestedRegion(Rect? requestedRegion)
    {
        if (requestedRegion is { } value && !RenderRectValidation.IsFiniteNonNegative(value))
        {
            throw new ArgumentException(
                "A requested region must be finite and have non-negative dimensions.",
                nameof(requestedRegion));
        }
    }
}
