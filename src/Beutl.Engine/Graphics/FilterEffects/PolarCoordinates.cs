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
        uniform float2 origin;
        uniform float2 size;
        uniform float amount;
        uniform float2 sourceRatio;
        uniform float2 sourceOffset;

        float2 halfTexel() {
            return min(0.5 / sourceRatio, size * 0.5);
        }

        half4 sampleLinear(float2 p) {
            float2 inset = halfTexel();
            p = clamp(p, origin + inset, origin + size - inset);
            float2 t = p * sourceRatio + sourceOffset - 0.5;
            float2 b = floor(t);
            half2 f = half2(t - b);
            float2 c = (b + 0.5 - sourceOffset) / sourceRatio;
            float2 d = 1.0 / sourceRatio;
            half4 top = mix(src.eval(c), src.eval(c + float2(d.x, 0.0)), f.x);
            half4 bottom = mix(src.eval(c + float2(0.0, d.y)), src.eval(c + d), f.x);
            return mix(top, bottom, f.y);
        }

        """;

    private static readonly SkslSource s_rectangularToPolar = SkslSource.WholeSource(
        SharedSource +
        """
        half4 main(float2 coord) {
            float2 local = coord - origin;
            if (any(lessThan(local, float2(0.0))) || any(greaterThanEqual(local, size))) {
                return half4(0.0);
            }

            float2 halfSize = size * 0.5;
            float2 q = (local - halfSize) / halfSize;
            float radius = length(q);
            float angle = radius == 0.0 ? 0.0 : atan(q.x, -q.y);
            float u = fract(angle / 6.28318530718);
            float2 sample = mix(coord, origin + size * float2(u, radius), amount);

            // Only the rim can map past the source; fade it over one source texel instead of cutting it.
            // The clamp in sampleLinear keeps the 12 o'clock seam and the collapsed top row opaque.
            float coverage = clamp((origin.y + size.y - sample.y) * sourceRatio.y + 0.5, 0.0, 1.0);
            return sampleLinear(sample) * half(coverage);
        }
        """);

    private static readonly SkslSource s_polarToRectangular = SkslSource.WholeSource(
        SharedSource +
        """
        half4 main(float2 coord) {
            float2 local = coord - origin;
            if (any(lessThan(local, float2(0.0))) || any(greaterThanEqual(local, size))) {
                return half4(0.0);
            }

            float2 uv = local / size;
            float angle = uv.x * 6.28318530718;
            float2 halfSize = size * 0.5;
            float2 mapped = origin + halfSize + halfSize * uv.y * float2(sin(angle), -cos(angle));
            return sampleLinear(mix(coord, mapped, amount));
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

    private readonly record struct WarpParameters(PolarCoordinatesDirection Direction, float Amount)
    {
        public bool HitTest(RenderHitTestContext context, Point point)
        {
            Rect bounds = context.OutputBounds;
            if (!bounds.ContainsExclusive(point))
                return false;

            var local = new Vector2(point.X - bounds.X, point.Y - bounds.Y);
            var size = new Vector2(bounds.Width, bounds.Height);
            Vector2 halfSize = size * 0.5f;

            // The shaders use the same normalized mapping and lerp. The half-pixel inset
            // belongs to raster sampling, while hit tests use continuous logical coordinates.
            Vector2 mapped;
            if (Direction == PolarCoordinatesDirection.PolarToRectangular)
            {
                Vector2 uv = local / size;
                (float sin, float cos) = MathF.SinCos(uv.X * MathF.Tau);
                mapped = halfSize + halfSize * uv.Y * new Vector2(sin, -cos);
            }
            else
            {
                Vector2 q = (local - halfSize) / halfSize;
                float radius = q.Length();
                float angle = radius == 0f ? 0f : MathF.Atan2(q.X, -q.Y);
                float turns = angle / MathF.Tau;
                mapped = size * new Vector2(turns - MathF.Floor(turns), radius);
            }

            Vector2 sample = Vector2.Lerp(local, mapped, Amount);
            return RenderHitTestContract.AnyInputAccepts(
                context.Inputs, bounds.Position + new Vector(sample.X, sample.Y));
        }
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

            var parameters = new WarpParameters(Direction, amount);
            SkslSource source = Direction == PolarCoordinatesDirection.PolarToRectangular
                ? s_polarToRectangular
                : s_rectangularToPolar;
            context.Shader(ShaderDescription.WholeSource(
                source,
                RenderBoundsContract.FullInput,
                bindings =>
                {
                    bindings.Uniform("amount", amount);
                    bindings.Uniform("sourceRatio", Vector2.One, BindSourceRatio);
                    bindings.Uniform("sourceOffset", Vector2.Zero, BindSourceOffset);
                    // Anchor the warp to the full logical bounds at the current working density.
                    bindings.Uniform("origin", 0, static (writer, _, execution) =>
                    {
                        Vector origin = execution.OutputBounds.Position - execution.OutputPlacement.LogicalOrigin;
                        writer.Set(new Vector2(origin.X, origin.Y) * execution.OutputPlacement.Scale.Value);
                    });
                    bindings.Uniform("size", 0, static (writer, _, execution) =>
                    {
                        Size size = execution.OutputBounds.Size;
                        writer.Set(new Vector2(size.Width, size.Height) * execution.OutputPlacement.Scale.Value);
                    });
                },
                SKShaderTileMode.Decal,
                hitTest: RenderHitTestContract.Custom(
                    parameters,
                    static (state, hitTest, point) => state.HitTest(hitTest, point))));
        }
    }
}
