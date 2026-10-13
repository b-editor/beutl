using System.ComponentModel.DataAnnotations;
using System.Numerics;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shaders;
using Beutl.Language;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.ColorShift), ResourceType = typeof(GraphicsStrings))]
public partial class ColorShift : FilterEffect
{
    private const string ShaderSource =
        """
        uniform shader src;
        uniform float2 redOffset;
        uniform float2 greenOffset;
        uniform float2 blueOffset;
        uniform float2 alphaOffset;

        half4 main(float2 fragCoord) {
            float2 redCoord   = fragCoord - redOffset;
            float2 greenCoord = fragCoord - greenOffset;
            float2 blueCoord  = fragCoord - blueOffset;
            float2 alphaCoord = fragCoord - alphaOffset;

            float red   = src.eval(redCoord).r;
            float green = src.eval(greenCoord).g;
            float blue  = src.eval(blueCoord).b;
            float alpha = src.eval(alphaCoord).a;

            return half4(red, green, blue, alpha);
        }
        """;

    private static readonly SkslSource s_shaderSource =
        new(ShaderSource, ShaderDescriptionKind.WholeSource);

    public ColorShift()
    {
        ScanProperties<ColorShift>();
    }

    [Display(Name = nameof(GraphicsStrings.ColorShift_RedOffset), ResourceType = typeof(GraphicsStrings))]
    public IProperty<PixelPoint> RedOffset { get; } = Property.CreateAnimatable<PixelPoint>();

    [Display(Name = nameof(GraphicsStrings.ColorShift_GreenOffset), ResourceType = typeof(GraphicsStrings))]
    public IProperty<PixelPoint> GreenOffset { get; } = Property.CreateAnimatable<PixelPoint>();

    [Display(Name = nameof(GraphicsStrings.ColorShift_BlueOffset), ResourceType = typeof(GraphicsStrings))]
    public IProperty<PixelPoint> BlueOffset { get; } = Property.CreateAnimatable<PixelPoint>();

    [Display(Name = nameof(GraphicsStrings.ColorShift_AlphaOffset), ResourceType = typeof(GraphicsStrings))]
    public IProperty<PixelPoint> AlphaOffset { get; } = Property.CreateAnimatable<PixelPoint>();

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        var offsets = new ColorShiftOffsets(
            r.RedOffset,
            r.GreenOffset,
            r.BlueOffset,
            r.AlphaOffset);
        RenderBoundsContract bounds = RenderBoundsContract.Create(
            offsets,
            static (state, bounds) => state.TransformBounds(bounds),
            static (state, bounds) => state.GetRequiredInputBounds(bounds));

        context.Shader(ShaderDescription.WholeSource(
            s_shaderSource,
            bounds,
            bindings =>
            {
                BindOffset(bindings, "redOffset", r.RedOffset);
                BindOffset(bindings, "greenOffset", r.GreenOffset);
                BindOffset(bindings, "blueOffset", r.BlueOffset);
                BindOffset(bindings, "alphaOffset", r.AlphaOffset);
            },
            SKShaderTileMode.Decal,
            hitTest: offsets.MovesContent
                ? RenderHitTestContract.Custom(
                    offsets,
                    static (state, context, point) => state.HitTest(context, point))
                : null));
    }

    private static void BindOffset(ShaderBindingBuilder bindings, string name, PixelPoint value)
    {
        bindings.Uniform(
            name,
            new Vector2(value.X, value.Y),
            static (writer, offset, execution) => writer.Set(offset * execution.WorkingScale));
    }

    private readonly record struct ColorShiftOffsets(
        PixelPoint RedOffset,
        PixelPoint GreenOffset,
        PixelPoint BlueOffset,
        PixelPoint AlphaOffset)
    {
        /// <remarks>
        /// Every offset at zero reads the pixel it writes, which forwarding the query to the input already answers.
        /// </remarks>
        public bool MovesContent
            => RedOffset != default
               || GreenOffset != default
               || BlueOffset != default
               || AlphaOffset != default;

        /// <remarks>
        /// A point hits when the input covers any of its four channel translations. A zero channel there can
        /// still leave the pixel clear; that over-claim is accepted, as a stage must answer for what it produced.
        /// </remarks>
        public bool HitTest(RenderHitTestContext context, Point point)
            => ReadsCoveredInput(context, point, RedOffset)
               || ReadsCoveredInput(context, point, GreenOffset)
               || ReadsCoveredInput(context, point, BlueOffset)
               || ReadsCoveredInput(context, point, AlphaOffset);

        private static bool ReadsCoveredInput(
            RenderHitTestContext context,
            Point point,
            PixelPoint offset)
        {
            // Decal sampling leaves everything outside the input transparent, so an input that misses the
            // translated point contributes nothing there.
            return RenderHitTestContract.AnyInputAccepts(context.Inputs, point - offset.ToPoint(1));
        }

        public Rect TransformBounds(Rect bounds)
            => bounds.Translate(RedOffset.ToPoint(1))
                .Union(bounds.Translate(GreenOffset.ToPoint(1)))
                .Union(bounds.Translate(BlueOffset.ToPoint(1)))
                .Union(bounds.Translate(AlphaOffset.ToPoint(1)));

        public Rect GetRequiredInputBounds(Rect bounds)
            => bounds.Translate(-RedOffset.ToPoint(1))
                .Union(bounds.Translate(-GreenOffset.ToPoint(1)))
                .Union(bounds.Translate(-BlueOffset.ToPoint(1)))
                .Union(bounds.Translate(-AlphaOffset.ToPoint(1)));
    }
}
