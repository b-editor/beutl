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
        uniform float4 radial;
        uniform float4 angular;
        uniform float2 options;
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

            float innerR = radial.x;
            float outerR = radial.y;
            bool invertRadius = radial.z > 0.5;
            float edgeMode = radial.w;
            float angleTurns = angular.x;
            float sweepTurns = angular.y;
            float repeatCount = angular.z;
            bool reverseAngle = angular.w > 0.5;
            bool mirrorMode = options.x > 0.5;
            float amount = options.y;

            float2 q = (coord - center) / radii;
            float radius = length(q);
            float angle = radius == 0.0 ? 0.0 : atan(q.x, -q.y);
            float rawTurns = angle / 6.28318530718 - angleTurns;
            if (reverseAngle) {
                rawTurns = -rawTurns;
            }
            float t = fract(rawTurns);

            bool isFullCircle = sweepTurns >= 1.0 - 1e-6;
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

            float span = outerR - innerR;
            bool degenerateRadius = abs(span) < 1e-6;
            float v;
            if (degenerateRadius) {
                v = edgeMode < 0.5 ? 2.0 : (radius < innerR ? 0.0 : 1.0);
            } else {
                v = (radius - innerR) / span;
            }
            if (invertRadius && !(degenerateRadius && edgeMode < 0.5)) {
                v = 1.0 - v;
            }

            float uMapped;
            float vMapped;
            if (edgeMode < 0.5) {
                uMapped = applyRepeat(clamp(uArc, 0.0, 1.0), repeatCount, mirrorMode);
                vMapped = v;
            } else {
                float uBounded = isFullCircle ? uArc : applyEdge(uArc, edgeMode);
                uMapped = applyRepeat(uBounded, repeatCount, mirrorMode);
                vMapped = applyEdge(v, edgeMode);
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
                // Keep the collapsed pole opaque when it maps onto an edge of the source,
                // while still fading samples that fall outside the source when amount < 1.
                bool minAtOrigin = !degenerateRadius && ((!invertRadius && innerR == 0.0) || (invertRadius && outerR == 0.0));
                bool maxAtOrigin = !degenerateRadius && ((invertRadius && innerR == 0.0) || (!invertRadius && outerR == 0.0));
                float minYOffset = minAtOrigin ? 1.0 : 0.5;
                float maxYOffset = maxAtOrigin ? 1.0 : 0.5;
                coverage *= clamp((sample.y - inputOrigin.y) * sourceRatio.y + minYOffset, 0.0, 1.0);
                coverage *= clamp((inputOrigin.y + inputSize.y - sample.y) * sourceRatio.y + maxYOffset, 0.0, 1.0);
            } else if (amount < 1.0) {
                float2 norm = (sample - inputOrigin) / inputSize;
                sample = inputOrigin + inputSize * float2(applyEdge(norm.x, edgeMode), applyEdge(norm.y, edgeMode));
            }

            return sampleLinear(sample) * half(coverage);
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

            float innerR = radial.x;
            float outerR = radial.y;
            bool invertRadius = radial.z > 0.5;
            float edgeMode = radial.w;
            float angleTurns = angular.x;
            float sweepTurns = angular.y;
            float repeatCount = angular.z;
            bool reverseAngle = angular.w > 0.5;
            bool mirrorMode = options.x > 0.5;
            float amount = options.y;

            float2 uv = outLocal / outputSize;
            float u = applyRepeat(uv.x, repeatCount, mirrorMode);
            float arcTurns = u * sweepTurns;
            float angle = (angleTurns + (reverseAngle ? -arcTurns : arcTurns)) * 6.28318530718;
            float v = invertRadius ? 1.0 - uv.y : uv.y;
            float r = mix(innerR, outerR, v);
            float2 mapped = center + radii * r * float2(sin(angle), -cos(angle));
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

    [Display(Name = nameof(GraphicsStrings.Center), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<RelativePoint> Center { get; } = Property.CreateAnimatable(RelativePoint.Center);

    [Display(Name = nameof(GraphicsStrings.Angle), ResourceType = typeof(GraphicsStrings))]
    [Unit(PropertyUnit.Degrees)]
    public IExpressionProperty<float> Angle { get; } = Property.CreateAnimatable(0f);

    [Display(Name = nameof(GraphicsStrings.PolarCoordinates_AspectMode), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<PolarCoordinatesAspectMode> AspectMode { get; } =
        Property.CreateExpression(PolarCoordinatesAspectMode.Stretch);

    [Display(Name = nameof(GraphicsStrings.PolarCoordinates_InnerRadius), ResourceType = typeof(GraphicsStrings))]
    [Range(0, float.MaxValue)]
    [Unit(PropertyUnit.Percent)]
    public IExpressionProperty<float> InnerRadius { get; } = Property.CreateAnimatable(0f);

    [Display(Name = nameof(GraphicsStrings.PolarCoordinates_OuterRadius), ResourceType = typeof(GraphicsStrings))]
    [Range(0, float.MaxValue)]
    [Unit(PropertyUnit.Percent)]
    public IExpressionProperty<float> OuterRadius { get; } = Property.CreateAnimatable(100f);

    [Display(Name = nameof(GraphicsStrings.PolarCoordinates_InvertRadius), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<bool> InvertRadius { get; } = Property.CreateAnimatable(false);

    [Display(Name = nameof(GraphicsStrings.PolarCoordinates_ReverseAngle), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<bool> ReverseAngle { get; } = Property.CreateAnimatable(false);

    [Display(Name = nameof(GraphicsStrings.PolarCoordinates_Repeat), ResourceType = typeof(GraphicsStrings))]
    [Range(0.01, float.MaxValue), NumberStep(0.1, 0.01)]
    public IExpressionProperty<float> Repeat { get; } = Property.CreateAnimatable(1f);

    [Display(Name = nameof(GraphicsStrings.PolarCoordinates_Mirror), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<bool> Mirror { get; } = Property.CreateAnimatable(false);

    [Display(Name = nameof(GraphicsStrings.PolarCoordinates_SweepAngle), ResourceType = typeof(GraphicsStrings))]
    [Range(0, 360)]
    [Unit(PropertyUnit.Degrees)]
    public IExpressionProperty<float> SweepAngle { get; } = Property.CreateAnimatable(360f);

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

    private static Vector2 ResolveRadii(PolarCoordinatesAspectMode aspectMode, Vector2 size)
    {
        Vector2 halfSize = Vector2.Max(size * 0.5f, new Vector2(1e-6f));
        return aspectMode switch
        {
            PolarCoordinatesAspectMode.Contain => new Vector2(MathF.Min(halfSize.X, halfSize.Y)),
            PolarCoordinatesAspectMode.Cover => new Vector2(MathF.Max(halfSize.X, halfSize.Y)),
            _ => halfSize,
        };
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
        RelativePoint Center,
        float AngleTurns,
        PolarCoordinatesAspectMode AspectMode,
        float InnerRadius,
        float OuterRadius,
        bool InvertRadius,
        bool ReverseAngle,
        float Repeat,
        bool Mirror,
        float SweepTurns,
        PolarCoordinatesEdgeMode EdgeMode,
        bool ClipToBounds)
    {
        public Rect TransformBounds(Rect inputBounds)
        {
            if (ClipToBounds
                || Direction != PolarCoordinatesDirection.RectangularToPolar
                || inputBounds.IsInvalid
                || inputBounds.Width <= 0f
                || inputBounds.Height <= 0f)
            {
                return inputBounds;
            }

            float maxRadius = MathF.Max(InnerRadius, OuterRadius);
            if (!float.IsFinite(maxRadius) || maxRadius <= 1e-4f)
                return inputBounds;

            if (Center == RelativePoint.Center
                && AspectMode == PolarCoordinatesAspectMode.Stretch
                && maxRadius == 1f)
            {
                return inputBounds;
            }

            var inputSize = new Vector2(inputBounds.Width, inputBounds.Height);
            Vector2 radii = ResolveRadii(AspectMode, inputSize);
            float rx = radii.X * maxRadius;
            float ry = radii.Y * maxRadius;
            if (!float.IsFinite(rx) || !float.IsFinite(ry) || rx <= 0f || ry <= 0f)
                return inputBounds;

            Vector2 localCenter = ResolveLocalCenter(Center, inputBounds.Size);
            float cx = inputBounds.X + localCenter.X;
            float cy = inputBounds.Y + localCenter.Y;
            float x = cx - rx;
            float y = cy - ry;
            float width = rx * 2f;
            float height = ry * 2f;
            if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(width) || !float.IsFinite(height)
                || width <= 0f || height <= 0f)
            {
                return inputBounds;
            }

            var polarBounds = new Rect(x, y, width, height);
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
            Vector2 radii = ResolveRadii(AspectMode, inSize);
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
                float arcTurns = u * SweepTurns;
                float angle = (AngleTurns + (ReverseAngle ? -arcTurns : arcTurns)) * MathF.Tau;
                float v = InvertRadius ? 1f - uv.Y : uv.Y;
                float r = float.Lerp(InnerRadius, OuterRadius, v);
                (float sin, float cos) = MathF.SinCos(angle);
                Vector2 mapped = center + radii * r * new Vector2(sin, -cos);
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
                Vector2 q = (inLocal - center) / radii;
                float radius = q.Length();
                float angle = radius == 0f ? 0f : MathF.Atan2(q.X, -q.Y);
                float rawTurns = angle / MathF.Tau - AngleTurns;
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

                float span = OuterRadius - InnerRadius;
                bool degenerateRadius = MathF.Abs(span) < 1e-6f;
                float v = degenerateRadius
                    ? (EdgeMode == PolarCoordinatesEdgeMode.Transparent ? 2f : (radius < InnerRadius ? 0f : 1f))
                    : (radius - InnerRadius) / span;
                if (InvertRadius && !(degenerateRadius && EdgeMode == PolarCoordinatesEdgeMode.Transparent))
                    v = 1f - v;

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
                    bool minAtOrigin = !degenerateRadius && ((!InvertRadius && InnerRadius == 0f) || (InvertRadius && OuterRadius == 0f));
                    bool maxAtOrigin = !degenerateRadius && ((InvertRadius && InnerRadius == 0f) || (!InvertRadius && OuterRadius == 0f));
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

            float angleTurns = NormalizeTurns(Angle);
            PolarCoordinatesAspectMode aspectMode = Enum.IsDefined(AspectMode)
                ? AspectMode
                : PolarCoordinatesAspectMode.Stretch;
            PolarCoordinatesEdgeMode edgeMode = Enum.IsDefined(EdgeMode)
                ? EdgeMode
                : PolarCoordinatesEdgeMode.Transparent;
            float innerRadius = float.IsFinite(InnerRadius) ? MathF.Max(0f, InnerRadius) / 100f : 0f;
            float outerRadius = float.IsFinite(OuterRadius) ? MathF.Max(0f, OuterRadius) / 100f : 1f;
            float repeat = float.IsFinite(Repeat) ? MathF.Max(0.01f, Repeat) : 1f;
            float sweepTurns = float.IsFinite(SweepAngle) ? Math.Clamp(SweepAngle, 0f, 360f) / 360f : 1f;

            var parameters = new WarpParameters(
                Direction,
                amount,
                Center,
                angleTurns,
                aspectMode,
                innerRadius,
                outerRadius,
                InvertRadius,
                ReverseAngle,
                repeat,
                Mirror,
                sweepTurns,
                edgeMode,
                ClipToBounds);
            var radialUniform = new Vector4(innerRadius, outerRadius, InvertRadius ? 1f : 0f, (float)edgeMode);
            var angularUniform = new Vector4(angleTurns, sweepTurns, repeat, ReverseAngle ? 1f : 0f);
            var optionsUniform = new Vector2(Mirror ? 1f : 0f, amount);
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
                    bindings.Uniform("radial", radialUniform);
                    bindings.Uniform("angular", angularUniform);
                    bindings.Uniform("options", optionsUniform);
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
                    bindings.Uniform("radii", aspectMode, static (writer, mode, execution) =>
                    {
                        Size size = execution.InputBounds.Size;
                        Vector2 deviceSize = new Vector2(size.Width, size.Height) * execution.OutputPlacement.Scale.Value;
                        writer.Set(ResolveRadii(mode, deviceSize));
                    });
                },
                SKShaderTileMode.Decal,
                hitTest: RenderHitTestContract.Custom(
                    parameters,
                    static (state, hitTest, point) => state.HitTest(hitTest, point))));
        }
    }
}
