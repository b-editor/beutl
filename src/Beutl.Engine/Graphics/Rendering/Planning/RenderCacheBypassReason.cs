namespace Beutl.Graphics.Rendering;

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
