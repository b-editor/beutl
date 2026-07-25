namespace Beutl.Audio.Effects;

// Shared ranges and defaults keep validation and per-sample clamps aligned.
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

    // A finite floor lets the closed gate ramp without clicking; 0 dB disables gating.
    public const float MinRangeDb = -100f;
    public const float MaxRangeDb = 0f;
    public const float DefaultRangeDb = -60f;
}
