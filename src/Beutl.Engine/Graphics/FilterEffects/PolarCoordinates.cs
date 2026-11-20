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
    // A partial request can rasterize padding around the logical output, so both shaders clip to it.
    private static readonly SkslSource s_rectangularToPolar = SkslSource.WholeSource(
        """
        uniform shader src;
        uniform float2 origin;
        uniform float2 size;
        uniform float amount;

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
            float2 mapped = size * float2(u, radius);

            // In working pixels, inset the angular seam and the collapsed top row to texel centers.
            float2 inset = min(float2(0.5), halfSize);
            mapped.x = clamp(mapped.x, inset.x, size.x - inset.x);
            mapped.y = max(mapped.y, inset.y);
            return src.eval(mix(coord, origin + mapped, amount));
        }
        """);

    private static readonly SkslSource s_polarToRectangular = SkslSource.WholeSource(
        """
        uniform shader src;
        uniform float2 origin;
        uniform float2 size;
        uniform float amount;

        half4 main(float2 coord) {
            float2 local = coord - origin;
            if (any(lessThan(local, float2(0.0))) || any(greaterThanEqual(local, size))) {
                return half4(0.0);
            }

            float2 uv = local / size;
            float angle = uv.x * 6.28318530718;
            float2 halfSize = size * 0.5;
            float2 mapped = origin + halfSize + halfSize * uv.y * float2(sin(angle), -cos(angle));
            return src.eval(mix(coord, mapped, amount));
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
