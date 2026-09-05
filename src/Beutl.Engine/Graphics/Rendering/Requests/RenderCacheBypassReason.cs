namespace Beutl.Graphics.Rendering.Requests;

internal enum RenderCacheBypassReason : byte
{
    None,
    CacheDisabled,
    MetadataOnlyPurpose,
    PersistentLookupDisabled,
    CapturePublicationDisabled,
    EmptyRequirement,
    OutsideCacheRules,
    ExternalInputExceedsBufferBudget,
    TargetTokenDependency,
    RawTargetWork,
    DeviceGridDependentOutput,
    NotMaterializable,
    UnstableBoundaryPlan,
}
