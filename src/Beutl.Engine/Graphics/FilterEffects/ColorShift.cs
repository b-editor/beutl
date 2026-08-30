using System.ComponentModel.DataAnnotations;
using System.Numerics;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
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
            ShaderSource,
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
            BindScaledOffset);
    }

    private static void BindScaledOffset(
        ShaderUniformWriter writer,
        Vector2 value,
        ShaderExecutionContext context)
        => writer.Set(value * context.WorkingScale);

    private readonly record struct ColorShiftOffsets(
        PixelPoint RedOffset,
        PixelPoint GreenOffset,
        PixelPoint BlueOffset,
        PixelPoint AlphaOffset)
    {
        /// <remarks>
        /// Every offset at zero makes the entry point read the pixel it writes, which is what forwarding the
        /// query to the input already answers - and forwarding answers it through the input's own rule, which
        /// no contract stated here can be more exact than.
        /// </remarks>
        public bool MovesContent
            => RedOffset != default
               || GreenOffset != default
               || BlueOffset != default
               || AlphaOffset != default;

        /// <remarks>
        /// The entry point evaluates <c>src</c> at <c>fragCoord</c> minus each channel's own offset and takes
        /// one channel from each result, so the points it reads for one output pixel are exactly those four
        /// translations of it, and the pixel carries something wherever any of them did. Alpha alone would not
        /// answer this: it comes from <c>alphaOffset</c> only, so a colour offset paints a channel over a
        /// transparent pixel, and premultiplied compositing adds that colour to whatever is behind it.
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
            Point source = point - offset.ToPoint(1);
            IReadOnlyList<RenderHitTestInput> inputs = context.Inputs;
            for (int index = 0; index < inputs.Count; index++)
            {
                if (inputs[index].HitTest(source))
                    return true;
            }

            return false;
        }

        public Rect TransformBounds(Rect bounds)
            => bounds.Translate(RedOffset.ToPoint(1))
                .Union(bounds.Translate(GreenOffset.ToPoint(1)))
                .Union(bounds.Translate(BlueOffset.ToPoint(1)))
                .Union(bounds.Translate(AlphaOffset.ToPoint(1)));

        public Rect GetRequiredInputBounds(Rect bounds)
            => bounds.Translate(ToInverseOffset(RedOffset))
                .Union(bounds.Translate(ToInverseOffset(GreenOffset)))
                .Union(bounds.Translate(ToInverseOffset(BlueOffset)))
                .Union(bounds.Translate(ToInverseOffset(AlphaOffset)));

        private static Point ToInverseOffset(PixelPoint value) => new(-value.X, -value.Y);
    }
}
