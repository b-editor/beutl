namespace Beutl.Audio.Effects;

// Single source of truth for the noise gate's ranges and defaults, shared by GateEffect's
// [Range] declarations and GateNode's per-sample clamps so the two cannot drift.
// GateEffectTests.GateParameters_RangeIsConsistent asserts each entry's consistency.
internal static class GateParameters
{
    public const float MinThresholdDb = -100f;
    public const float MaxThresholdDb = 0f;
    public const float DefaultThresholdDb = -40f;

    public const float MinAttackMs = 0.1f;
    public const float MaxAttackMs = 500f;
    public const float DefaultAttackMs = 1f;

    public const float MinHoldMs = 0f;
    public const float MaxHoldMs = 5000f;
    public const float DefaultHoldMs = 10f;

    public const float MinReleaseMs = 1f;
    public const float MaxReleaseMs = 5000f;
    public const float DefaultReleaseMs = 100f;

    // Attenuation applied while the gate is closed. 0 dB disables gating (the closed floor equals the
    // open level), and more negative values attenuate harder. Kept above -∞ so a closed gate ramps to
    // a finite floor rather than clicking to hard mute.
    public const float MinRangeDb = -100f;
    public const float MaxRangeDb = 0f;
    public const float DefaultRangeDb = -60f;
}
