using System.ComponentModel.DataAnnotations;
using System.Numerics;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shaders;
using Beutl.Language;
using Beutl.Utilities;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.DiagonalClipping), ResourceType = typeof(GraphicsStrings))]
public sealed partial class DiagonalClipping : FilterEffect
{
    private const string ShaderSource =
        """
        uniform shader src;
        uniform float2 center;
        uniform float2 normal;
        uniform float position;
        uniform float width;
        uniform float workingScale;

        // Exact area of the pixel square on the positive side of the line. The central branch also
        // handles horizontal/vertical lines without dividing by their zero minor component.
        float coverage(float distance) {
            float2 n = abs(normal);
            float major = max(n.x, n.y);
            float minor = min(n.x, n.y);
            float d = abs(distance) * workingScale;
            float halfSpan = (major + minor) * 0.5;
            float tail = 0.0;
            if (d < halfSpan) {
                if (d <= (major - minor) * 0.5) {
                    tail = 0.5 - d / major;
                } else {
                    float edge = halfSpan - d;
                    tail = edge * edge / (2.0 * major * minor);
                }
            }
            return distance >= 0.0 ? 1.0 - tail : tail;
        }

        half4 main(float2 coord) {
            float distance = dot((coord - center) / workingScale, normal) - position;
            float alpha;
            if (width == 0.0) {
                alpha = coverage(distance);
            } else {
                float halfWidth = abs(width) * 0.5;
                alpha = clamp(coverage(distance + halfWidth) - coverage(distance - halfWidth), 0.0, 1.0);
                if (width < 0.0) {
                    alpha = 1.0 - alpha;
                }
            }
            return src.eval(coord) * half(alpha);
        }
        """;

    private static readonly SkslSource s_shaderSource =
        new(ShaderSource, ShaderDescriptionKind.WholeSource);

    public DiagonalClipping()
    {
        ScanProperties<DiagonalClipping>();
    }

    [Display(Name = nameof(GraphicsStrings.Angle), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<float> Angle { get; } = Property.CreateAnimatable(45f);

    [Display(Name = nameof(GraphicsStrings.Position), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<float> Position { get; } = Property.CreateAnimatable(0f);

    [Display(Name = nameof(GraphicsStrings.Width),
        Description = nameof(GraphicsStrings.DiagonalClipping_Width_Description), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<float> Width { get; } = Property.CreateAnimatable(0f);

    private readonly record struct ClipParameters(Vector2 Normal, float Position, float Width)
    {
        public bool HitTest(RenderHitTestContext context, Point point)
        {
            if (!context.OutputBounds.ContainsExclusive(point))
                return false;

            Vector offset = point - context.OutputBounds.Center;
            float distance = (offset.X * Normal.X) + (offset.Y * Normal.Y) - Position;
            bool kept = Width switch
            {
                > 0f => MathF.Abs(distance) <= Width / 2f,
                < 0f => MathF.Abs(distance) >= -Width / 2f,
                _ => distance >= 0f,
            };
            return kept && RenderHitTestContract.AnyInputAccepts(context.Inputs, point);
        }
    }

    public new partial class Resource
    {
        protected override void ApplyTo(FilterEffectContext context)
        {
            float angle = float.IsFinite(Angle) ? Angle % 360f : 0f;
            (float sin, float cos) = MathF.SinCos(MathUtilities.Deg2Rad(angle));
            var parameters = new ClipParameters(
                new Vector2(-sin, cos),
                float.IsFinite(Position) ? Position : 0f,
                float.IsFinite(Width) ? Width : 0f);

            context.Shader(ShaderDescription.WholeSource(
                s_shaderSource,
                RenderBoundsContract.Identity,
                bindings =>
                {
                    bindings.Uniform("normal", parameters.Normal);
                    bindings.Uniform("position", parameters.Position);
                    bindings.Uniform("width", parameters.Width);
                    bindings.Uniform("workingScale", 0, static (writer, _, execution) => writer.Set(execution.WorkingScale));
                    bindings.Uniform("center", 0, static (writer, _, execution) =>
                    {
                        // Anchor to the complete logical source, including when only part of it is requested.
                        Vector center = execution.OutputBounds.Center - execution.LogicalOrigin;
                        writer.Set(new Vector2(center.X, center.Y) * execution.WorkingScale);
                    });
                },
                SKShaderTileMode.Decal,
                hitTest: RenderHitTestContract.Custom(
                    parameters,
                    static (state, hitTest, point) => state.HitTest(hitTest, point))));
        }
    }
}
