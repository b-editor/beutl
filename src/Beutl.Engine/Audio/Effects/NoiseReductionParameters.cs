using Beutl.Audio.Graph;

namespace Beutl.Audio.Effects;

// Single source of truth for the noise reduction ranges and defaults, shared by NoiseReductionEffect's
// [Range] declarations and NoiseReductionNode's per-frame clamps so the two cannot drift.
internal static class NoiseReductionParameters
{
    // The deepest attenuation applied to bins judged to be noise; 0 dB passes the input unchanged.
    public const float MinReductionDb = 0f;
    public const float MaxReductionDb = 48f;
    public const float DefaultReductionDb = 12f;

    // Raises the estimated noise floor before it is compared with the input. The estimate is calibrated
    // to the mean noise power, so 0 dB removes stationary noise; higher values also catch fluctuating
    // noise at the cost of quiet details.
    public const float MinSensitivityDb = 0f;
    public const float MaxSensitivityDb = 24f;
    public const float DefaultSensitivityDb = 3f;

    // Trades a faster response to quiet sounds (0%) for steadier residual noise without warbling (100%).
    public const float MinSmoothing = 0f;
    public const float MaxSmoothing = 100f;
    public const float DefaultSmoothing = 50f;

    // How much past audio the noise floor is learned from. Sounds that sustain longer than this are
    // treated as noise, so music with long notes needs a longer time than speech.
    public const float MinAdaptationSeconds = 0.5f;
    public const float MaxAdaptationSeconds = 10f;
    public const float DefaultAdaptationSeconds = 2f;

    public static NoiseReductionSettings Normalize(
        float reductionDb,
        float sensitivityDb,
        float smoothing,
        float adaptationSeconds)
    {
        return new NoiseReductionSettings(
            ClampFinite(reductionDb, DefaultReductionDb, MinReductionDb, MaxReductionDb),
            ClampFinite(sensitivityDb, DefaultSensitivityDb, MinSensitivityDb, MaxSensitivityDb),
            ClampFinite(smoothing, DefaultSmoothing, MinSmoothing, MaxSmoothing),
            ClampFinite(adaptationSeconds, DefaultAdaptationSeconds, MinAdaptationSeconds, MaxAdaptationSeconds));
    }

    // Math.Clamp keeps NaN, which would poison every later frame's gains.
    private static float ClampFinite(float value, float fallback, float min, float max)
        => float.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}
