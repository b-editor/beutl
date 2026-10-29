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
    public IExpressionProperty<PixelPoint> RedOffset { get; } = Property.CreateAnimatable<PixelPoint>();

    [Display(Name = nameof(GraphicsStrings.ColorShift_GreenOffset), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<PixelPoint> GreenOffset { get; } = Property.CreateAnimatable<PixelPoint>();

    [Display(Name = nameof(GraphicsStrings.ColorShift_BlueOffset), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<PixelPoint> BlueOffset { get; } = Property.CreateAnimatable<PixelPoint>();

    [Display(Name = nameof(GraphicsStrings.ColorShift_AlphaOffset), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<PixelPoint> AlphaOffset { get; } = Property.CreateAnimatable<PixelPoint>();

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

    public new partial class Resource
    {
        protected override void ApplyTo(FilterEffectContext context)
        {
            var offsets = new ColorShiftOffsets(
                RedOffset,
                GreenOffset,
                BlueOffset,
                AlphaOffset);
            RenderBoundsContract bounds = RenderBoundsContract.Create(
                offsets,
                static (state, bounds) => state.TransformBounds(bounds),
                static (state, bounds) => state.GetRequiredInputBounds(bounds));

            context.Shader(ShaderDescription.WholeSource(
                s_shaderSource,
                bounds,
                bindings =>
                {
                    BindOffset(bindings, "redOffset", RedOffset);
                    BindOffset(bindings, "greenOffset", GreenOffset);
                    BindOffset(bindings, "blueOffset", BlueOffset);
                    BindOffset(bindings, "alphaOffset", AlphaOffset);
                },
                SKShaderTileMode.Decal));
        }
    }
}
