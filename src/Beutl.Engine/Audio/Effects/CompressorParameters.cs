namespace Beutl.Audio.Effects;

// Single source of truth for the compressor's ranges, defaults and static curve, shared by
// CompressorEffect's [Range] declarations, CompressorNode's per-sample clamp and the curve editor so
// they cannot drift. CompressorEffectTests.CompressorParameters_RangeIsConsistent asserts each entry's
// consistency.
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

    // Soft-knee gain computer (Reece/Giannoulis), shared by CompressorNode's per-sample path and the
    // transfer-curve editor so the plotted curve cannot drift from what the DSP applies. ratio must be
    // at least MinRatio; callers clamp it, which keeps the slope non-negative.
    internal static float ComputeGainReductionDb(float envelopeDb, float thresholdDb, float ratio, float kneeDb)
    {
        float slope = 1f - 1f / ratio;
        if (kneeDb > 0f)
        {
            float halfKnee = kneeDb * 0.5f;
            float diff = envelopeDb - thresholdDb;
            if (diff <= -halfKnee)
            {
                return 0f;
            }
            if (diff < halfKnee)
            {
                // Quadratic across the knee: 0 at -halfKnee, slope * halfKnee at +halfKnee, with
                // matching derivatives at both ends so the curve stays smooth.
                float x = diff + halfKnee;
                return slope * x * x / (2f * kneeDb);
            }
            return slope * diff;
        }

        return envelopeDb > thresholdDb ? slope * (envelopeDb - thresholdDb) : 0f;
    }

    // The static transfer curve: the input level after gain reduction and makeup gain. The editor draws
    // this, so it is the one definition of the curve's shape.
    internal static float ComputeOutputDb(float inputDb, float thresholdDb, float ratio, float kneeDb, float makeupDb)
        => inputDb - ComputeGainReductionDb(inputDb, thresholdDb, ratio, kneeDb) + makeupDb;
}
