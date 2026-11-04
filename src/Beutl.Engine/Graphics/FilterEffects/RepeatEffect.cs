using System.ComponentModel.DataAnnotations;
using System.Numerics;
using System.Runtime.InteropServices;
using Beutl.Engine;
using Beutl.Graphics.Shaders;
using Beutl.Language;

namespace Beutl.Graphics.Effects;

/// <summary>
/// Repeats the picture in a grid around where it is. Every pixel of the grid reads one place in the picture, so the
/// cost does not grow with the number of copies. Effects before this one are repeated with the picture, and effects
/// after it, and the object's transform, apply to the grid as a whole.
/// </summary>
[Display(Name = nameof(GraphicsStrings.RepeatEffect), ResourceType = typeof(GraphicsStrings))]
public sealed partial class RepeatEffect : FilterEffect
{
    private const int MaxCount = 100;

    public RepeatEffect() => ScanProperties<RepeatEffect>();

    [Display(Name = nameof(GraphicsStrings.RepeatEffect_Columns), ResourceType = typeof(GraphicsStrings))]
    [Range(1, MaxCount)]
    public IExpressionProperty<int> Columns { get; } = Property.CreateAnimatable(3);

    [Display(Name = nameof(GraphicsStrings.RepeatEffect_Rows), ResourceType = typeof(GraphicsStrings))]
    [Range(1, MaxCount)]
    public IExpressionProperty<int> Rows { get; } = Property.CreateAnimatable(3);

    [Display(Name = nameof(GraphicsStrings.RepeatEffect_Spacing), Description = nameof(GraphicsStrings.RepeatEffect_Spacing_Description),
        ResourceType = typeof(GraphicsStrings))]
    [Range(typeof(Size), "0,0", "max,max")]
    public IExpressionProperty<Size> Spacing { get; } = Property.CreateAnimatable(Size.Empty);

    [Display(Name = nameof(GraphicsStrings.Offset), Description = nameof(GraphicsStrings.RepeatEffect_Offset_Description),
        ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<Point> Offset { get; } = Property.CreateAnimatable(default(Point));

    [Display(Name = nameof(GraphicsStrings.RepeatEffect_Mirror), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<RepeatEffectMirror> Mirror { get; } = Property.CreateAnimatable(RepeatEffectMirror.None);

    public new partial class Resource
    {
        protected override void ApplyTo(FilterEffectContext context)
        {
            if (!float.IsFinite(Spacing.Width) || !float.IsFinite(Spacing.Height)
                || !float.IsFinite(Offset.X) || !float.IsFinite(Offset.Y))
            {
                return;
            }

            if (Columns == 1 && Rows == 1 && Offset == default)
                return;

            context.ShaderGraph(
                new RepeatValues(
                    default,
                    new Vector2(Columns, Rows),
                    new Vector2(Spacing.Width, Spacing.Height),
                    new Vector2(Offset.X, Offset.Y),
                    new Vector2(
                        Mirror is RepeatEffectMirror.Columns or RepeatEffectMirror.Both ? 1 : 0,
                        Mirror is RepeatEffectMirror.Rows or RepeatEffectMirror.Both ? 1 : 0)),
                static (graph, values) =>
                {
                    // The copies and the gaps between them, around the middle of the picture. A split picture counts
                    // as one, so each of its pieces can land anywhere in the grid.
                    ShaderImage output = graph.Image2D("output", graph.Source);
                    Vector2 gaps = (values.Count - Vector2.One) * values.Spacing / 2;
                    Matrix copies = Matrix.CreateScale(values.Count.X, values.Count.Y);
                    output.BoundsFromGroup = true;
                    output.BoundsTransforms = [copies * Matrix.CreateTranslation(-gaps.X, -gaps.Y), copies * Matrix.CreateTranslation(gaps.X, gaps.Y)];
                    graph.Fullscreen("repeat", s_program, pass =>
                    {
                        pass.SampledImage(0, graph.Source);
                        pass.ImageParameters(1, graph.Source);
                        pass.ImageParameters(2, output);
                        pass.PushConstants(values);
                        pass.BoundsOriginConstants(RelativePoint.Center, 0);
                        pass.PartialRendering();
                        pass.ColorAttachment(0, output);
                    });
                    return graph.Output(output);
                });
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct RepeatValues(Vector2 Center, Vector2 Count, Vector2 Spacing, Vector2 Offset, Vector2 Mirror);

    // The copies go on past the bounds, so the offset scrolls them without a jump and flipped ones keep alternating.
    private static readonly ShaderProgram s_program = ShaderProgram.Fullscreen(new ShaderSource("""
        #version 450
        layout(binding = 0) uniform sampler2D image;
        layout(binding = 1, std140) uniform Source { vec4 logical; vec4 raster; vec4 size; } src;
        layout(binding = 2, std140) uniform Destination { vec4 logical; vec4 raster; vec4 size; } dst;
        layout(push_constant) uniform Values { vec2 center; vec2 count; vec2 spacing; vec2 offset; vec2 mirror; } pc;
        layout(location = 0) out vec4 color;
        void main() {
            vec2 position = dst.raster.xy + gl_FragCoord.xy / dst.size.z - dst.logical.xy;
            vec2 picture = (dst.logical.zw - (pc.count - 1) * pc.spacing) / pc.count;
            vec2 pitch = picture + pc.spacing;
            // Half of the gap on each side belongs to a copy, so both of its edges can fade.
            vec2 grid = position - pc.offset + pc.spacing / 2;
            vec2 index = floor(grid / pitch);
            vec2 local = grid - index * pitch - pc.spacing / 2;
            local = mix(local, picture - local, mod(index, 2) * pc.mirror);
            vec2 origin = pc.center - picture / 2;
            vec2 touching = step(pc.spacing, vec2(0));
            // A piece of a split picture covers only part of its copy, and one piece takes each pixel where two meet.
            // A flipped copy, or rounding, can put a pixel right at the far end, which still belongs to the copy.
            vec2 nearest = origin + clamp(local, vec2(0), max(picture - 0.02, vec2(0)));
            vec2 piece = floor((nearest - src.logical.xy + 0.01) / src.logical.zw) * touching;
            // Copies that touch must not fade where they meet, so they stay on the texels the picture fills completely.
            vec2 first = ceil((src.logical.xy - src.raster.xy) * src.size.z - 0.01) + 0.5;
            vec2 last = floor((src.logical.xy + src.logical.zw - src.raster.xy) * src.size.z + 0.01) - 0.5;
            vec2 uv = (origin + local - src.raster.xy) / src.raster.zw;
            uv = mix(uv, clamp(uv, first / src.size.xy, max(first, last) / src.size.xy), touching * step(first, last));
            // The image rounds the bounds out to whole pixels, where the next copy along must not show.
            color = piece == vec2(0) && floor(position / dst.logical.zw) == vec2(0) ? texture(image, uv) : vec4(0);
        }
        """));
}
