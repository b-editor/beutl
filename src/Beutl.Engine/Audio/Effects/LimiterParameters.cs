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

    // Recomputes the ceiling from sampleRate (rather than reading LimiterNode's cached
    // _maxLookaheadSamples) so latency can be reported before the node initializes its buffers.
    // Must stay in sync with the clamp in LimiterNode.Derive; kept out of Derive, which runs
    // per-sample on the animated path and cannot pay this recompute.
    public static int ToLatencySamples(float lookaheadMs, int sampleRate)
    {
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate), "Sample rate must be positive.");

        int maxLookaheadSamples = Math.Max(1, (int)(MaxLookaheadMs / 1000f * sampleRate) + 1);
        if (!float.IsFinite(lookaheadMs))
            lookaheadMs = MinLookaheadMs;

        lookaheadMs = Math.Clamp(lookaheadMs, MinLookaheadMs, MaxLookaheadMs);
        return Math.Clamp((int)(lookaheadMs / 1000f * sampleRate), 0, maxLookaheadSamples - 1);
    }
}
