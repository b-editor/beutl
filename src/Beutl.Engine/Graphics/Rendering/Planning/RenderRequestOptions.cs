using Beutl.Graphics.Rendering.Cache;

namespace Beutl.Graphics.Rendering;

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
        IRenderPipelineDiagnosticsState? diagnostics = null,
        NestedRenderTargetBinding? targetBinding = null)
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
        OutputScale = SanitizeOutputScale(outputScale);
        MaxWorkingScale = RenderScaleUtilities.SanitizeMaxWorkingScale(maxWorkingScale);
        RenderCacheOptions sourceCachePolicy = cachePolicy ?? RenderCacheOptions.Default;
        CachePolicy = new RenderCacheOptions(sourceCachePolicy.IsEnabled, sourceCachePolicy.Rules);
        FusionMode = fusionMode;
        Owner = owner ?? new RenderRequestOwner();
        OwnsOwner = owner is null;
        Diagnostics = diagnostics ?? new RenderPipelineDiagnosticsState();
        TargetBinding = targetBinding;
        PlanIdentity = new RenderRequestPlanIdentity(
            Purpose,
            FusionMode,
            CachePolicy.IsEnabled,
            CachePolicy.Rules);
    }

    public RenderIntent Intent { get; }

    public RenderRequestPurpose Purpose { get; }

    public Rect? TargetDomain { get; }

    public Rect? RequestedRegion { get; }

    public float OutputScale { get; }

    public float MaxWorkingScale { get; }

    public RenderCacheOptions CachePolicy { get; }

    public FusionMode FusionMode { get; }

    public RenderRequestOwner Owner { get; }

    public IRenderPipelineDiagnosticsState Diagnostics { get; }

    public NestedRenderTargetBinding? TargetBinding { get; }

    public RenderRequestPlanIdentity PlanIdentity { get; }

    internal bool OwnsOwner { get; }

    internal RenderRequestOptions? NestedPolicyParent { get; private set; }

    public RenderRequestOptions CreateNested(
        NestedRenderTargetBinding targetBinding,
        Rect? targetDomain = null,
        Rect? requestedRegion = null)
        => CreateNestedCore(
            targetBinding,
            targetDomain,
            requestedRegion,
            OutputScale,
            MaxWorkingScale);

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

        return CreateNestedCore(
            targetBinding,
            targetDomain,
            requestedRegion,
            workingScale,
            workingScale);
    }

    private RenderRequestOptions CreateNestedCore(
        NestedRenderTargetBinding targetBinding,
        Rect? targetDomain,
        Rect? requestedRegion,
        float outputScale,
        float maxWorkingScale)
    {
        ArgumentNullException.ThrowIfNull(targetBinding);
        var nested = new RenderRequestOptions(
            Intent,
            Purpose,
            targetDomain ?? TargetDomain,
            requestedRegion ?? RequestedRegion,
            outputScale,
            maxWorkingScale,
            CachePolicy,
            FusionMode,
            Owner,
            Diagnostics,
            targetBinding);
        nested.NestedPolicyParent = this;
        return nested;
    }

    private static float SanitizeOutputScale(float outputScale)
        => float.IsFinite(outputScale) && outputScale > 0 ? outputScale : 1;

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

internal readonly record struct RenderRequestPlanIdentity(
    RenderRequestPurpose Purpose,
    FusionMode FusionMode,
    bool CacheEnabled,
    RenderCacheRules CacheRules);

internal enum FusionMode : byte
{
    Enabled,
    Disabled,
}
