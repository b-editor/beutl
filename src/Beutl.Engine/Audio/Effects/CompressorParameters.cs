using System.Diagnostics;

namespace Beutl.Audio.Effects;

// Single source of truth for the compressor's parameter ranges and defaults. CompressorEffect
// references these in its [Range] / Property.CreateAnimatable declarations and CompressorNode
// references the same values when clamping per-sample animated inputs, so the two cannot drift.
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

    static CompressorParameters()
    {
        // Run once at module load so an inconsistent Min/Default/Max edit is caught immediately
        // in debug builds rather than producing subtle audio glitches at runtime.
        AssertRange(MinThresholdDb, DefaultThresholdDb, MaxThresholdDb, "Threshold");
        AssertRange(MinRatio, DefaultRatio, MaxRatio, "Ratio");
        AssertRange(MinAttackMs, DefaultAttackMs, MaxAttackMs, "Attack");
        AssertRange(MinReleaseMs, DefaultReleaseMs, MaxReleaseMs, "Release");
        AssertRange(MinKneeDb, DefaultKneeDb, MaxKneeDb, "Knee");
        AssertRange(MinMakeupGainDb, DefaultMakeupGainDb, MaxMakeupGainDb, "MakeupGain");
        Debug.Assert(MinRatio >= 1f, "MinRatio must stay >= 1f or the slope formula amplifies.");
    }

    private static void AssertRange(float min, float def, float max, string name)
    {
        Debug.Assert(min < max, $"{name}: Min ({min}) must be strictly less than Max ({max}).");
        Debug.Assert(min <= def && def <= max, $"{name}: Default ({def}) must lie in [{min}, {max}].");
    }
}
