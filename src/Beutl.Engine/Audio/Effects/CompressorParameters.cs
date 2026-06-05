namespace Beutl.Audio.Effects;

// Single source of truth for the compressor's parameter ranges and defaults. CompressorEffect
// references these in its [Range] / Property.CreateAnimatable declarations and CompressorNode
// references the same values when clamping per-sample animated inputs, so the two cannot drift.
//
// The Min/Default/Max consistency of every entry below is asserted by
// CompressorEffectTests.CompressorParameters_RangeIsConsistent — a plain unit test, not a runtime
// hook — so a future edit that puts a default outside its range fails CI with a named test rather
// than a load-time Debug.Assert.
internal static class CompressorParameters
{
    public const float MinThresholdDb = -60f;
    public const float MaxThresholdDb = 0f;
    public const float DefaultThresholdDb = -20f;

    // MinRatio must stay >= 1f. The slope formula `1 - 1/Ratio` becomes negative below 1, which
    // would amplify above-threshold signals instead of compressing them; CompressorNode.Sanitize
    // relies on this invariant to make the slope safe without an additional guard.
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
