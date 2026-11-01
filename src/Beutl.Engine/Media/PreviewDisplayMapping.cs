namespace Beutl.Media;

/// <summary>
/// Fits preview values into what the display can show. Each pixel's largest component rolls off into the display's
/// headroom along the ITU-R BT.2390 EETF in the PQ domain, and the other components scale with it, so hue holds and
/// saturated color keeps its gradation where a per-channel clip would flatten it.
/// </summary>
/// <remarks>
/// Values are in units of reference white (<see cref="ReferenceWhiteNits"/>). The curve keeps gradation for content up
/// to <see cref="ContentPeak"/>, and anything brighter lands on the display's peak.
/// </remarks>
/// <param name="Knee">The value below which nothing changes.</param>
/// <param name="Ks">BT.2390's KS: the knee's PQ signal over the content peak's.</param>
/// <param name="MaxLum">BT.2390's maxLum: the display peak's PQ signal over the content peak's.</param>
/// <param name="SourcePq">The PQ signal of the content peak.</param>
internal readonly record struct PreviewDisplayMapping(float Knee, float Ks, float MaxLum, float SourcePq)
{
    /// <summary>
    /// The luminance of linear 1.0 in HDR content: the BT.2408 reference white, which Skia converts PQ and HLG to.
    /// </summary>
    public const float ReferenceWhiteNits = 203f;

    /// <summary>The brightest content the curve keeps gradation for, in nits: the usual HDR10 mastering peak.</summary>
    public const float ContentPeakNits = 1000f;

    /// <summary><see cref="ContentPeakNits"/> in units of reference white.</summary>
    public const float ContentPeak = ContentPeakNits / ReferenceWhiteNits;

    /// <summary>
    /// The functions both preview paths compile, the HDR present shader (GLSL) and the SDR preview effect (SkSL), so
    /// they keep to the scalar subset the two languages share. <c>displayMapGain</c> takes a pixel's largest
    /// component and then the four fields of a mapping in order, and returns the factor to scale the pixel by.
    /// </summary>
    public const string ShaderSource = """
        const float PQ_M1 = 0.1593017578125;
        const float PQ_M2 = 78.84375;
        const float PQ_C1 = 0.8359375;
        const float PQ_C2 = 18.8515625;
        const float PQ_C3 = 18.6875;
        // Reference white over the PQ peak: 203 nit out of 10000.
        const float PQ_SCALE = 0.0203;

        float pqEncode(float v) {
            float y = pow(clamp(v * PQ_SCALE, 0.0, 1.0), PQ_M1);
            return pow((PQ_C1 + PQ_C2 * y) / (1.0 + PQ_C3 * y), PQ_M2);
        }

        float pqDecode(float e) {
            float p = pow(clamp(e, 0.0, 1.0), 1.0 / PQ_M2);
            return pow(max(p - PQ_C1, 0.0) / (PQ_C2 - PQ_C3 * p), 1.0 / PQ_M1) / PQ_SCALE;
        }

        float displayMapGain(float peak, float knee, float ks, float maxLum, float sourcePq) {
            if (peak <= knee) return 1.0;
            float t = clamp((pqEncode(peak) / sourcePq - ks) / (1.0 - ks), 0.0, 1.0);
            float t2 = t * t;
            float t3 = t2 * t;
            float e2 = (2.0 * t3 - 3.0 * t2 + 1.0) * ks + (t3 - 2.0 * t2 + t) * (1.0 - ks)
                + (3.0 * t2 - 2.0 * t3) * maxLum;
            return pqDecode(e2 * sourcePq) / peak;
        }
        """;

    private const float PqM1 = 0.1593017578125f;
    private const float PqM2 = 78.84375f;
    private const float PqC1 = 0.8359375f;
    private const float PqC2 = 18.8515625f;
    private const float PqC3 = 18.6875f;
    private const float PqScale = ReferenceWhiteNits / 10000f;

    /// <summary>Leaves every value as it is.</summary>
    public static PreviewDisplayMapping Identity { get; } = new(float.MaxValue, 0f, 1f, 1f);

    /// <summary>
    /// Returns the mapping for a display whose brightest value is <paramref name="headroom"/>, or <see cref="Identity"/>
    /// when the headroom is unknown (zero or less) or already reaches <see cref="ContentPeak"/>.
    /// </summary>
    public static PreviewDisplayMapping For(float headroom)
    {
        if (!(headroom > 0f) || headroom >= ContentPeak)
            return Identity;

        float sourcePq = PqEncode(ContentPeak);
        float maxLum = PqEncode(headroom) / sourcePq;
        float ks = MathF.Max(1.5f * maxLum - 0.5f, 0f);
        return new PreviewDisplayMapping(PqDecode(ks * sourcePq), ks, maxLum, sourcePq);
    }

    private static float PqEncode(float value)
    {
        float y = MathF.Pow(Math.Clamp(value * PqScale, 0f, 1f), PqM1);
        return MathF.Pow((PqC1 + PqC2 * y) / (1f + PqC3 * y), PqM2);
    }

    private static float PqDecode(float signal)
    {
        float p = MathF.Pow(Math.Clamp(signal, 0f, 1f), 1f / PqM2);
        return MathF.Pow(MathF.Max(p - PqC1, 0f) / (PqC2 - PqC3 * p), 1f / PqM1) / PqScale;
    }
}
