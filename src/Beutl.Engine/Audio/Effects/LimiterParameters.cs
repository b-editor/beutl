namespace Beutl.Audio.Effects;

// Single source of truth for the limiter's ranges and defaults, shared by LimiterEffect's
// [Range] declarations and LimiterNode's per-sample clamps so the two cannot drift.
internal static class LimiterParameters
{
    public const float MinThresholdDb = -60f;
    public const float MaxThresholdDb = 0f;
    // Threshold defaults to -1 dB (not 0 dB) so a single-sound output peaks at ~0.891 linear,
    // below the always-on master limiter's 1.0 ceiling — no double-limiting on the default path.
    public const float DefaultThresholdDb = -1.0f;

    public const float MinReleaseMs = 1f;
    public const float MaxReleaseMs = 5000f;
    public const float DefaultReleaseMs = 50f;

    // Lookahead defaults to 0 ms so the effect is sample-accurate / A/V-synchronized out of the box
    // (Beutl's inline audio graph has no plugin-delay-compensation).
    public const float MinLookaheadMs = 0f;
    public const float MaxLookaheadMs = 20f;
    public const float DefaultLookaheadMs = 0f;

    public const float MinMakeupGainDb = -24f;
    public const float MaxMakeupGainDb = 24f;
    public const float DefaultMakeupGainDb = 0f;
}
