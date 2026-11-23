using System.ComponentModel.DataAnnotations;
using System.Numerics;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shaders;
using Beutl.Language;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.PolarCoordinates), ResourceType = typeof(GraphicsStrings))]
public sealed partial class PolarCoordinates : FilterEffect
{
    // Whole-source stages sample src with nearest filtering, which leaves stair-stepped color boundaries
    // after a warp, so the shaders filter bilinearly in the source's own texel grid (see MosaicEffect).
    // Samples are clamped to texel centers inside the logical bounds: a partial request can rasterize
    // padding, and the source raster can extend past fractional logical bounds.
    private const string SharedSource =
        """
        uniform shader src;
        uniform float2 inputOrigin;
        uniform float2 inputSize;
        uniform float2 outputOrigin;
        uniform float2 outputSize;
        uniform float2 center;
        uniform float2 radii;
        uniform float4 angular;
        uniform float4 options;
        uniform float amount;
        uniform float2 sourceRatio;
        uniform float2 sourceOffset;

        half4 sampleLinear(float2 p) {
            // In texel index space, where texel k's center is at k; only centers inside the logical input bounds are tapped.
            float2 first = ceil(inputOrigin * sourceRatio + sourceOffset - 0.5);
            float2 last = max(first, floor((inputOrigin + inputSize) * sourceRatio + sourceOffset - 0.5));
            float2 t = clamp(p * sourceRatio + sourceOffset - 0.5, first, last);
            float2 b0 = floor(t);
            half2 f = half2(t - b0);
            float2 b1 = min(b0 + 1.0, last);
            float2 c0 = (b0 + 0.5 - sourceOffset) / sourceRatio;
            float2 c1 = (b1 + 0.5 - sourceOffset) / sourceRatio;
            half4 top = mix(src.eval(c0), src.eval(float2(c1.x, c0.y)), f.x);
            half4 bottom = mix(src.eval(float2(c0.x, c1.y)), src.eval(c1), f.x);
            return mix(top, bottom, f.y);
        }

        float pingPong(float x) {
            float p = mod(x, 2.0);
            return p <= 1.0 ? p : 2.0 - p;
        }

        float applyEdge(float x, float mode) {
            if (mode > 2.5) {
                return pingPong(x);
            }
            if (mode > 1.5) {
                return fract(x);
            }
            if (mode > 0.5) {
                return clamp(x, 0.0, 1.0);
            }
            return x;
        }

        float applyRepeat(float u, float repeatCount, bool mirrorMode) {
            float scaled = u * repeatCount;
            if (mirrorMode) {
                return pingPong(scaled);
            }
            if (u >= 1.0 && repeatCount >= 1.0) {
                float f = fract(scaled);
                return f == 0.0 ? 1.0 : f;
            }
            return fract(scaled);
        }

        """;

    private static readonly SkslSource s_rectangularToPolar = SkslSource.WholeSource(
        SharedSource +
        """
        half4 main(float2 coord) {
            float2 outLocal = coord - outputOrigin;
            if (any(lessThan(outLocal, float2(0.0))) || any(greaterThanEqual(outLocal, outputSize))) {
                return half4(0.0);
            }

            float innerR = radii.x;
            float outerR = radii.y;
            float angleTurns = angular.x;
            float swirlTurns = angular.y;
            float sweepTurns = angular.z;
            float repeatCount = angular.w;
            bool invertRadius = options.x > 0.5;
            bool reverseAngle = options.y > 0.5;
            bool mirrorMode = options.z > 0.5;
            float edgeMode = options.w;

            float2 q = coord - center;
            float radius = length(q);
            float angle = radius == 0.0 ? 0.0 : atan(q.x, -q.y);

            float band = outerR - innerR;
            bool degenerateRadius = abs(band) < 1e-6;
            float vLinear;
            if (degenerateRadius) {
                vLinear = edgeMode < 0.5 ? 2.0 : (radius < innerR ? 0.0 : 1.0);
            } else {
                vLinear = (radius - innerR) / band;
            }

            // Default (invertRadius == false) places the top edge (v = 0) on the outside and bottom (v = 1) on the inside.
            float v = (degenerateRadius && edgeMode < 0.5)
                ? 2.0
                : (invertRadius ? vLinear : 1.0 - vLinear);

            float swirlOffset = degenerateRadius ? 0.0 : swirlTurns * (vLinear - 1.0);
            float baseRawTurns = angle / 6.28318530718 - angleTurns - swirlOffset;
            if (reverseAngle) {
                baseRawTurns = -baseRawTurns;
            }

            bool isFullCircle = sweepTurns >= 1.0 - 1e-6;
            float vMapped = edgeMode < 0.5 ? v : applyEdge(v, edgeMode);

            // Estimate how many source texels span one output pixel along the arc so compressed
            // inner regions and thin text stay smooth instead of aliasing.
            float texelCircumference = 6.28318530718 * max(radius, 1.0);
            float effectiveSweep = max(sweepTurns, 1e-4);
            float footprintTurns = 1.0 / texelCircumference;
            float footprintTexels = footprintTurns * (inputSize.x * sourceRatio.x) * (repeatCount / effectiveSweep);
            int tapCount = (amount >= 1.0 - 1e-6 && footprintTexels > 1.25 && !degenerateRadius)
                ? int(clamp(ceil(footprintTexels), 2.0, 5.0))
                : 1;

            half4 accum = half4(0.0);
            for (int i = 0; i < 5; i++) {
                if (i >= tapCount) {
                    break;
                }
                float jitter = tapCount > 1 ? (((float(i) + 0.5) / float(tapCount)) - 0.5) * footprintTurns : 0.0;
                float t = fract(baseRawTurns + jitter);

                float uArc;
                if (isFullCircle) {
                    uArc = t;
                } else if (sweepTurns < 1e-6) {
                    uArc = edgeMode < 0.5 ? 2.0 : 0.0;
                } else {
                    float mid = sweepTurns * 0.5;
                    float tRel = fract(t - mid + 0.5) - 0.5 + mid;
                    uArc = tRel / sweepTurns;
                }

                float uMapped;
                if (edgeMode < 0.5) {
                    uMapped = applyRepeat(clamp(uArc, 0.0, 1.0), repeatCount, mirrorMode);
                } else {
                    float uBounded = isFullCircle ? uArc : applyEdge(uArc, edgeMode);
                    uMapped = applyRepeat(uBounded, repeatCount, mirrorMode);
                }

                float2 sample = mix(coord, inputOrigin + inputSize * float2(uMapped, vMapped), amount);

                float coverage = 1.0;
                if (edgeMode < 0.5) {
                    if (!isFullCircle) {
                        float arcSampleX = mix(coord.x, inputOrigin.x + inputSize.x * uArc, amount);
                        float covMinX = clamp((arcSampleX - inputOrigin.x) * sourceRatio.x + 0.5, 0.0, 1.0);
                        float covMaxX = clamp((inputOrigin.x + inputSize.x - arcSampleX) * sourceRatio.x + 0.5, 0.0, 1.0);
                        coverage *= min(covMinX, covMaxX);
                    } else if (amount < 1.0) {
                        // When outputBounds extends past inputBounds and amount < 1, sample.x can leave inputBounds.
                        // Offset by +1.0 so the 12 o'clock seam at sample.x == inputOrigin.x stays opaque.
                        float covMinX = clamp((sample.x - inputOrigin.x) * sourceRatio.x + 1.0, 0.0, 1.0);
                        float covMaxX = clamp((inputOrigin.x + inputSize.x - sample.x) * sourceRatio.x + 1.0, 0.0, 1.0);
                        coverage *= min(covMinX, covMaxX);
                    }
                    // Keep the collapsed pole opaque when innerR == 0 maps onto a source edge,
                    // while still fading samples that fall outside the source when amount < 1.
                    bool minAtOrigin = !degenerateRadius && invertRadius && innerR <= 0.0;
                    bool maxAtOrigin = !degenerateRadius && !invertRadius && innerR <= 0.0;
                    float minYOffset = minAtOrigin ? 1.0 : 0.5;
                    float maxYOffset = maxAtOrigin ? 1.0 : 0.5;
                    coverage *= clamp((sample.y - inputOrigin.y) * sourceRatio.y + minYOffset, 0.0, 1.0);
                    coverage *= clamp((inputOrigin.y + inputSize.y - sample.y) * sourceRatio.y + maxYOffset, 0.0, 1.0);
                } else if (amount < 1.0) {
                    float2 norm = (sample - inputOrigin) / inputSize;
                    sample = inputOrigin + inputSize * float2(applyEdge(norm.x, edgeMode), applyEdge(norm.y, edgeMode));
                }

                accum += sampleLinear(sample) * half(coverage);
            }

            return accum / half(tapCount);
        }
        """);

    private static readonly SkslSource s_polarToRectangular = SkslSource.WholeSource(
        SharedSource +
        """
        half4 main(float2 coord) {
            float2 outLocal = coord - outputOrigin;
            if (any(lessThan(outLocal, float2(0.0))) || any(greaterThanEqual(outLocal, outputSize))) {
                return half4(0.0);
            }

            float innerR = radii.x;
            float outerR = radii.y;
            float angleTurns = angular.x;
            float swirlTurns = angular.y;
            float sweepTurns = angular.z;
            float repeatCount = angular.w;
            bool invertRadius = options.x > 0.5;
            bool reverseAngle = options.y > 0.5;
            bool mirrorMode = options.z > 0.5;
            float edgeMode = options.w;

            float2 uv = outLocal / outputSize;
            float u = applyRepeat(uv.x, repeatCount, mirrorMode);
            float vLinear = invertRadius ? uv.y : 1.0 - uv.y;
            float r = mix(innerR, outerR, vLinear);
            float arcTurns = u * sweepTurns;
            float angle = (angleTurns + swirlTurns * (vLinear - 1.0) + (reverseAngle ? -arcTurns : arcTurns)) * 6.28318530718;
            float2 mapped = center + r * float2(sin(angle), -cos(angle));
            float2 sample = mix(coord, mapped, amount);

            if (edgeMode < 0.5) {
                // Fade each edge over one source texel when the mapped sample falls past the source bounds.
                float2 covMin = clamp((sample - inputOrigin) * sourceRatio + 0.5, 0.0, 1.0);
                float2 covMax = clamp((inputOrigin + inputSize - sample) * sourceRatio + 0.5, 0.0, 1.0);
                float2 cov = min(covMin, covMax);
                return sampleLinear(sample) * half(cov.x * cov.y);
            }

            float2 norm = (sample - inputOrigin) / inputSize;
            float2 wrapped = float2(applyEdge(norm.x, edgeMode), applyEdge(norm.y, edgeMode));
            return sampleLinear(inputOrigin + inputSize * wrapped);
        }
        """);

    public PolarCoordinates() => ScanProperties<PolarCoordinates>();

    [Display(Name = nameof(GraphicsStrings.PolarCoordinates_Direction), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<PolarCoordinatesDirection> Direction { get; } =
        Property.CreateExpression(PolarCoordinatesDirection.RectangularToPolar);

    [Display(Name = nameof(GraphicsStrings.PolarCoordinates_Amount), ResourceType = typeof(GraphicsStrings))]
    [Range(0, 100)]
    [Unit(PropertyUnit.Percent)]
    public IExpressionProperty<float> Amount { get; } = Property.CreateAnimatable(100f);

    [Display(Name = nameof(GraphicsStrings.PolarCoordinates_InnerRadius), ResourceType = typeof(GraphicsStrings))]
    [Range(0, float.MaxValue)]
    public IExpressionProperty<float> InnerRadius { get; } = Property.CreateAnimatable(0f);

    [Display(Name = nameof(GraphicsStrings.Scale), ResourceType = typeof(GraphicsStrings))]
    [Range(0, float.MaxValue)]
    [Unit(PropertyUnit.Percent)]
    public IExpressionProperty<float> Scale { get; } = Property.CreateAnimatable(100f);

    [Display(Name = nameof(GraphicsStrings.Angle), ResourceType = typeof(GraphicsStrings))]
    [Unit(PropertyUnit.Degrees)]
    public IExpressionProperty<float> Angle { get; } = Property.CreateAnimatable(0f);

    [Display(Name = nameof(GraphicsStrings.PolarCoordinates_Swirl), ResourceType = typeof(GraphicsStrings))]
    [Unit(PropertyUnit.Degrees)]
    public IExpressionProperty<float> Swirl { get; } = Property.CreateAnimatable(0f);

    [Display(Name = nameof(GraphicsStrings.PolarCoordinates_SweepAngle), ResourceType = typeof(GraphicsStrings))]
    [Range(0, 360)]
    [Unit(PropertyUnit.Degrees)]
    public IExpressionProperty<float> SweepAngle { get; } = Property.CreateAnimatable(360f);

    [Display(Name = nameof(GraphicsStrings.Center), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<RelativePoint> Center { get; } = Property.CreateAnimatable(RelativePoint.Center);

    [Display(Name = nameof(GraphicsStrings.PolarCoordinates_InvertRadius), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<bool> InvertRadius { get; } = Property.CreateAnimatable(false);

    [Display(Name = nameof(GraphicsStrings.PolarCoordinates_ReverseAngle), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<bool> ReverseAngle { get; } = Property.CreateAnimatable(false);

    [Display(Name = nameof(GraphicsStrings.PolarCoordinates_Repeat), ResourceType = typeof(GraphicsStrings))]
    [Range(0.01, float.MaxValue), NumberStep(0.1, 0.01)]
    public IExpressionProperty<float> Repeat { get; } = Property.CreateAnimatable(1f);

    [Display(Name = nameof(GraphicsStrings.PolarCoordinates_Mirror), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<bool> Mirror { get; } = Property.CreateAnimatable(false);

    [Display(Name = nameof(GraphicsStrings.PolarCoordinates_EdgeMode), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<PolarCoordinatesEdgeMode> EdgeMode { get; } =
        Property.CreateExpression(PolarCoordinatesEdgeMode.Transparent);

    [Display(Name = nameof(GraphicsStrings.PolarCoordinates_ClipToBounds), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<bool> ClipToBounds { get; } = Property.CreateAnimatable(false);

    private static Vector2 ResolveLocalCenter(RelativePoint center, Size size)
    {
        Point resolved = center.ToPixels(size);
        if (!float.IsFinite(resolved.X) || !float.IsFinite(resolved.Y))
            return new Vector2(size.Width * 0.5f, size.Height * 0.5f);

        return new Vector2(resolved.X, resolved.Y);
    }

    private static Vector2 ResolveRadii(
        PolarCoordinatesDirection direction, Size inputSize, float innerRadiusPx, float scale)
    {
        if (direction == PolarCoordinatesDirection.PolarToRectangular)
        {
            float baseOuter = MathF.Min(inputSize.Width, inputSize.Height) * 0.5f;
            return new Vector2(innerRadiusPx * scale, baseOuter * scale);
        }

        // Match AviUtl's polar transform sizing so both wide text and square inputs produce a natural circle:
        // outerR = (W + H + InnerRadius) / pi * scale, innerR = outerR * InnerRadius / (H + InnerRadius).
        float totalRadial = inputSize.Height + innerRadiusPx;
        float outerR = ((inputSize.Width + totalRadial) / MathF.PI) * scale;
        float innerR = totalRadial > 1e-6f ? outerR * (innerRadiusPx / totalRadial) : 0f;
        return new Vector2(innerR, outerR);
    }

    private static float NormalizeTurns(float degrees)
    {
        if (!float.IsFinite(degrees))
            return 0f;

        float turns = (degrees % 360f) / 360f;
        if (turns < 0f)
            turns += 1f;

        return turns >= 1f || turns == 0f ? 0f : turns;
    }

    private static float Fract(float x) => x - MathF.Floor(x);

    private static float PingPong(float x)
    {
        float p = x - 2f * MathF.Floor(x * 0.5f);
        return p <= 1f ? p : 2f - p;
    }

    private static float ApplyEdge(float x, PolarCoordinatesEdgeMode mode) => mode switch
    {
        PolarCoordinatesEdgeMode.Clamp => Math.Clamp(x, 0f, 1f),
        PolarCoordinatesEdgeMode.Repeat => Fract(x),
        PolarCoordinatesEdgeMode.Mirror => PingPong(x),
        _ => x,
    };

    private static float ApplyRepeat(float u, float repeatCount, bool mirrorMode)
    {
        float scaled = u * repeatCount;
        if (mirrorMode)
            return PingPong(scaled);

        if (u >= 1f && repeatCount >= 1f)
        {
            float f = Fract(scaled);
            return f == 0f ? 1f : f;
        }

        return Fract(scaled);
    }

    private readonly record struct WarpParameters(
        PolarCoordinatesDirection Direction,
        float Amount,
        float InnerRadius,
        float Scale,
        float AngleTurns,
        float SwirlTurns,
        float SweepTurns,
        RelativePoint Center,
        bool InvertRadius,
        bool ReverseAngle,
        float Repeat,
        bool Mirror,
        PolarCoordinatesEdgeMode EdgeMode,
        bool ClipToBounds)
    {
        public Rect TransformBounds(Rect inputBounds)
        {
            if (ClipToBounds
                || inputBounds.IsInvalid
                || inputBounds.Width <= 0f
                || inputBounds.Height <= 0f)
            {
                return inputBounds;
            }

            Vector2 radii = ResolveRadii(Direction, inputBounds.Size, InnerRadius, Scale);
            float innerR = radii.X;
            float outerR = radii.Y;

            if (Direction == PolarCoordinatesDirection.PolarToRectangular)
            {
                float band = MathF.Abs(outerR - innerR);
                float midRadius = (innerR + outerR) * 0.5f;
                float outWidth = MathF.Tau * midRadius * SweepTurns;
                float outHeight = band;
                if (!float.IsFinite(outWidth) || !float.IsFinite(outHeight)
                    || outWidth <= 1e-4f || outHeight <= 1e-4f)
                {
                    return inputBounds;
                }

                Point inputCenter = inputBounds.Center;
                float rectX = inputCenter.X - outWidth * 0.5f;
                float rectY = inputCenter.Y - outHeight * 0.5f;
                if (!float.IsFinite(rectX) || !float.IsFinite(rectY))
                    return inputBounds;

                if (Amount >= 1f - 1e-6f)
                    return new Rect(rectX, rectY, outWidth, outHeight);

                float x = float.Lerp(inputBounds.X, rectX, Amount);
                float y = float.Lerp(inputBounds.Y, rectY, Amount);
                float width = float.Lerp(inputBounds.Width, outWidth, Amount);
                float height = float.Lerp(inputBounds.Height, outHeight, Amount);
                return new Rect(x, y, width, height);
            }

            float maxRadius = MathF.Max(innerR, outerR);
            if (!float.IsFinite(maxRadius) || maxRadius <= 1e-4f)
                return inputBounds;

            Vector2 localCenter = ResolveLocalCenter(Center, inputBounds.Size);
            float cx = inputBounds.X + localCenter.X;
            float cy = inputBounds.Y + localCenter.Y;
            float px = cx - maxRadius;
            float py = cy - maxRadius;
            float diameter = maxRadius * 2f;
            if (!float.IsFinite(px) || !float.IsFinite(py) || !float.IsFinite(diameter) || diameter <= 0f)
                return inputBounds;

            var polarBounds = new Rect(px, py, diameter, diameter);
            if (EdgeMode == PolarCoordinatesEdgeMode.Transparent && Amount >= 1f - 1e-6f)
                return polarBounds;

            return inputBounds.Union(polarBounds);
        }

        public bool HitTest(RenderHitTestContext context, Point point)
        {
            Rect outputBounds = context.OutputBounds;
            if (!outputBounds.ContainsExclusive(point) || context.Inputs.Count == 0)
                return false;

            Rect inputBounds = context.Inputs[0].Bounds;
            for (int i = 1; i < context.Inputs.Count; i++)
                inputBounds = inputBounds.Union(context.Inputs[i].Bounds);

            if (inputBounds.IsInvalid || inputBounds.Width <= 0f || inputBounds.Height <= 0f)
                return false;

            var inLocal = new Vector2(point.X - inputBounds.X, point.Y - inputBounds.Y);
            var inSize = new Vector2(inputBounds.Width, inputBounds.Height);
            Vector2 radii = ResolveRadii(Direction, inputBounds.Size, InnerRadius, Scale);
            float innerR = radii.X;
            float outerR = radii.Y;
            Vector2 center = ResolveLocalCenter(Center, inputBounds.Size);

            // The shaders use the same normalized mapping and lerp. The half-pixel inset
            // belongs to raster sampling, while hit tests use continuous logical coordinates.
            Vector2 sample;
            if (Direction == PolarCoordinatesDirection.PolarToRectangular)
            {
                var outLocal = new Vector2(point.X - outputBounds.X, point.Y - outputBounds.Y);
                var outSize = new Vector2(outputBounds.Width, outputBounds.Height);
                Vector2 uv = outLocal / outSize;
                float u = ApplyRepeat(uv.X, Repeat, Mirror);
                float vLinear = InvertRadius ? uv.Y : 1f - uv.Y;
                float r = float.Lerp(innerR, outerR, vLinear);
                float arcTurns = u * SweepTurns;
                float angle = (AngleTurns + SwirlTurns * (vLinear - 1f) + (ReverseAngle ? -arcTurns : arcTurns)) * MathF.Tau;
                (float sin, float cos) = MathF.SinCos(angle);
                Vector2 mapped = center + r * new Vector2(sin, -cos);
                sample = Vector2.Lerp(inLocal, mapped, Amount);

                if (EdgeMode == PolarCoordinatesEdgeMode.Transparent)
                {
                    if (sample.X < 0f || sample.X >= inSize.X || sample.Y < 0f || sample.Y >= inSize.Y)
                        return false;
                }
                else
                {
                    Vector2 norm = sample / inSize;
                    sample = inSize * new Vector2(ApplyEdge(norm.X, EdgeMode), ApplyEdge(norm.Y, EdgeMode));
                    sample = ClampInsideExclusive(sample, inSize);
                }
            }
            else
            {
                Vector2 q = inLocal - center;
                float radius = q.Length();
                float angle = radius == 0f ? 0f : MathF.Atan2(q.X, -q.Y);

                float band = outerR - innerR;
                bool degenerateRadius = MathF.Abs(band) < 1e-6f;
                float vLinear = degenerateRadius
                    ? (EdgeMode == PolarCoordinatesEdgeMode.Transparent ? 2f : (radius < innerR ? 0f : 1f))
                    : (radius - innerR) / band;
                float v = (degenerateRadius && EdgeMode == PolarCoordinatesEdgeMode.Transparent)
                    ? 2f
                    : (InvertRadius ? vLinear : 1f - vLinear);

                float swirlOffset = degenerateRadius ? 0f : SwirlTurns * (vLinear - 1f);
                float rawTurns = angle / MathF.Tau - AngleTurns - swirlOffset;
                if (ReverseAngle)
                    rawTurns = -rawTurns;
                float t = Fract(rawTurns);

                bool isFullCircle = SweepTurns >= 1f - 1e-6f;
                float uArc;
                if (isFullCircle)
                {
                    uArc = t;
                }
                else if (SweepTurns < 1e-6f)
                {
                    uArc = EdgeMode == PolarCoordinatesEdgeMode.Transparent ? 2f : 0f;
                }
                else
                {
                    float mid = SweepTurns * 0.5f;
                    float tRel = Fract(t - mid + 0.5f) - 0.5f + mid;
                    uArc = tRel / SweepTurns;
                }

                float uMapped;
                float vMapped;
                if (EdgeMode == PolarCoordinatesEdgeMode.Transparent)
                {
                    if (!isFullCircle)
                    {
                        float arcSampleX = float.Lerp(inLocal.X, inSize.X * uArc, Amount);
                        if (arcSampleX < 0f || arcSampleX >= inSize.X)
                            return false;
                    }
                    uMapped = ApplyRepeat(Math.Clamp(uArc, 0f, 1f), Repeat, Mirror);
                    vMapped = v;
                }
                else
                {
                    float uBounded = isFullCircle ? uArc : ApplyEdge(uArc, EdgeMode);
                    uMapped = ApplyRepeat(uBounded, Repeat, Mirror);
                    vMapped = ApplyEdge(v, EdgeMode);
                }

                sample = Vector2.Lerp(inLocal, inSize * new Vector2(uMapped, vMapped), Amount);

                if (EdgeMode == PolarCoordinatesEdgeMode.Transparent)
                {
                    if (isFullCircle && Amount < 1f && (sample.X < 0f || sample.X > inSize.X))
                        return false;
                    bool maxAtOrigin = !degenerateRadius && !InvertRadius && innerR <= 0f;
                    if (sample.Y < 0f)
                        return false;
                    if (maxAtOrigin ? sample.Y > inSize.Y : sample.Y >= inSize.Y)
                        return false;
                    sample = ClampInsideExclusive(sample, inSize);
                }
                else
                {
                    if (Amount < 1f)
                    {
                        Vector2 norm = sample / inSize;
                        sample = inSize * new Vector2(ApplyEdge(norm.X, EdgeMode), ApplyEdge(norm.Y, EdgeMode));
                    }
                    sample = ClampInsideExclusive(sample, inSize);
                }
            }

            return RenderHitTestContract.AnyInputAccepts(
                context.Inputs, inputBounds.Position + new Vector(sample.X, sample.Y));
        }

        private static Vector2 ClampInsideExclusive(Vector2 sample, Vector2 size) => new(
            Math.Clamp(sample.X, 0f, MathF.BitDecrement(size.X)),
            Math.Clamp(sample.Y, 0f, MathF.BitDecrement(size.Y)));
    }

    // Source texels per stage pixel; without a source grid the stage's own grid is used instead.
    private static void BindSourceRatio(ShaderUniformWriter writer, Vector2 unit, ShaderExecutionContext context)
        => writer.Set(context.InputRasterOrigin is null
            ? unit
            : unit * (context.InputEffectiveScale.Value / context.OutputPlacement.Scale.Value));

    // The source texel coordinate of stage pixel 0.
    private static void BindSourceOffset(ShaderUniformWriter writer, Vector2 none, ShaderExecutionContext context)
    {
        if (context.InputRasterOrigin is not { } inputOrigin)
        {
            writer.Set(none);
            return;
        }

        float scale = context.InputEffectiveScale.Value;
        Point logicalOrigin = context.OutputPlacement.LogicalOrigin;
        writer.Set(new Vector2((logicalOrigin.X - inputOrigin.X) * scale, (logicalOrigin.Y - inputOrigin.Y) * scale));
    }

    public new partial class Resource
    {
        protected override void ApplyTo(FilterEffectContext context)
        {
            float amount = float.IsFinite(Amount) ? Math.Clamp(Amount / 100f, 0f, 1f) : 0f;
            if (amount == 0f)
                return;

            float innerRadius = float.IsFinite(InnerRadius) ? MathF.Max(0f, InnerRadius) : 0f;
            float scale = float.IsFinite(Scale) ? MathF.Max(0f, Scale) / 100f : 1f;
            float angleTurns = NormalizeTurns(Angle);
            float swirlTurns = float.IsFinite(Swirl) ? Swirl / 360f : 0f;
            float sweepTurns = float.IsFinite(SweepAngle) ? Math.Clamp(SweepAngle, 0f, 360f) / 360f : 1f;
            float repeat = float.IsFinite(Repeat) ? MathF.Max(0.01f, Repeat) : 1f;
            PolarCoordinatesEdgeMode edgeMode = Enum.IsDefined(EdgeMode)
                ? EdgeMode
                : PolarCoordinatesEdgeMode.Transparent;

            var parameters = new WarpParameters(
                Direction,
                amount,
                innerRadius,
                scale,
                angleTurns,
                swirlTurns,
                sweepTurns,
                Center,
                InvertRadius,
                ReverseAngle,
                repeat,
                Mirror,
                edgeMode,
                ClipToBounds);
            var angularUniform = new Vector4(angleTurns, swirlTurns, sweepTurns, repeat);
            var optionsUniform = new Vector4(
                InvertRadius ? 1f : 0f,
                ReverseAngle ? 1f : 0f,
                Mirror ? 1f : 0f,
                (float)edgeMode);
            SkslSource source = Direction == PolarCoordinatesDirection.PolarToRectangular
                ? s_polarToRectangular
                : s_rectangularToPolar;
            RenderBoundsContract boundsContract = RenderBoundsContract.CreateFullInput(
                parameters,
                static (state, bounds) => state.TransformBounds(bounds));
            context.Shader(ShaderDescription.WholeSource(
                source,
                boundsContract,
                bindings =>
                {
                    bindings.Uniform("angular", angularUniform);
                    bindings.Uniform("options", optionsUniform);
                    bindings.Uniform("amount", amount);
                    bindings.Uniform("sourceRatio", Vector2.One, BindSourceRatio);
                    bindings.Uniform("sourceOffset", Vector2.Zero, BindSourceOffset);
                    // Anchor the warp to the logical input and output bounds at the current working density.
                    bindings.Uniform("inputOrigin", 0, static (writer, _, execution) =>
                    {
                        Vector origin = execution.InputBounds.Position - execution.OutputPlacement.LogicalOrigin;
                        writer.Set(new Vector2(origin.X, origin.Y) * execution.OutputPlacement.Scale.Value);
                    });
                    bindings.Uniform("inputSize", 0, static (writer, _, execution) =>
                    {
                        Size size = execution.InputBounds.Size;
                        writer.Set(new Vector2(size.Width, size.Height) * execution.OutputPlacement.Scale.Value);
                    });
                    bindings.Uniform("outputOrigin", 0, static (writer, _, execution) =>
                    {
                        Vector origin = execution.OutputBounds.Position - execution.OutputPlacement.LogicalOrigin;
                        writer.Set(new Vector2(origin.X, origin.Y) * execution.OutputPlacement.Scale.Value);
                    });
                    bindings.Uniform("outputSize", 0, static (writer, _, execution) =>
                    {
                        Size size = execution.OutputBounds.Size;
                        writer.Set(new Vector2(size.Width, size.Height) * execution.OutputPlacement.Scale.Value);
                    });
                    bindings.Uniform("center", Center, static (writer, center, execution) =>
                    {
                        Rect bounds = execution.InputBounds;
                        Vector origin = bounds.Position - execution.OutputPlacement.LogicalOrigin;
                        Vector2 localCenter = ResolveLocalCenter(center, bounds.Size);
                        writer.Set((new Vector2(origin.X, origin.Y) + localCenter) * execution.OutputPlacement.Scale.Value);
                    });
                    bindings.Uniform("radii", (Direction, innerRadius, scale), static (writer, state, execution) =>
                    {
                        Vector2 logicalRadii = ResolveRadii(state.Direction, execution.InputBounds.Size, state.innerRadius, state.scale);
                        writer.Set(logicalRadii * execution.OutputPlacement.Scale.Value);
                    });
                },
                SKShaderTileMode.Decal,
                hitTest: RenderHitTestContract.Custom(
                    parameters,
                    static (state, hitTest, point) => state.HitTest(hitTest, point))));
        }
    }
}
