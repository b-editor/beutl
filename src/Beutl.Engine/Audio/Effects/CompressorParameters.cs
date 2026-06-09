namespace Beutl.Audio.Effects;

// Single source of truth for the compressor's ranges and defaults, shared by CompressorEffect's
// [Range] declarations and CompressorNode's per-sample clamps so the two cannot drift.
// CompressorEffectTests.CompressorParameters_RangeIsConsistent asserts each entry's consistency.
internal static class CompressorParameters
{
    public const float MinThresholdDb = -60f;
    public const float MaxThresholdDb = 0f;
    public const float DefaultThresholdDb = -20f;

    // MinRatio must stay >= 1f: below 1 the slope `1 - 1/Ratio` goes negative and amplifies instead
    // of compresses. CompressorNode.Sanitize relies on this to keep the slope safe without a guard.
    public const float MinRatio = 1f;
    public const float MaxRatio = 20f;
    public const float DefaultRatio = 4f;

    public const float MinAttackMs = 0.1f;
    public const float MaxAttackMs = 500f;
    public const float DefaultAttackMs = 10f;

    public const float MinReleaseMs = 1f;
    public const float MaxReleaseMs = 5000f;
    public const float DefaultReleaseMs = 100f;

    public const float MinKneeDb = 0f;
    public const float MaxKneeDb = 24f;
    public const float DefaultKneeDb = 6f;

    public const float MinMakeupGainDb = -24f;
    public const float MaxMakeupGainDb = 24f;
    public const float DefaultMakeupGainDb = 0f;
}
