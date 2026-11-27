using System.ComponentModel.DataAnnotations;
using System.Numerics;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shaders;
using Beutl.Language;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.MosaicEffect), ResourceType = typeof(GraphicsStrings))]
public partial class MosaicEffect : FilterEffect
{
    // The centre of an even tile is a pixel corner, where nearest sampling took either neighbour depending on
    // the texture size on AMD Radeon and Apple's Metal, so the centre of the source texel starting there is sampled,
    // snapped in the source's own grid, which an input at another density or grid phase does not share (#2723).
    private const string ShaderSource =
        """
        uniform shader src;
        uniform float2 origin;
        uniform float2 tileSize;
        uniform float2 sourceRatio;
        uniform float2 sourceOffset;

        half4 main(float2 fragCoord) {
            float2 blockIndex = floor((fragCoord - origin) / tileSize);
            float2 sampleCoord = (blockIndex * tileSize + tileSize * 0.5) + origin;
            float2 texel = floor(sampleCoord * sourceRatio + sourceOffset) + 0.5;
            return src.eval((texel - sourceOffset) / sourceRatio);
        }
        """;

    private static readonly SkslSource s_shaderSource =
        new(ShaderSource, ShaderDescriptionKind.WholeSource);

    public MosaicEffect()
    {
        ScanProperties<MosaicEffect>();
    }

    [Range(typeof(Size), "0.0001, 0.0001", "max,max")]
    [Display(Name = nameof(GraphicsStrings.MosaicEffect_TileSize), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<Size> TileSize { get; } = Property.CreateAnimatable(new Size(10, 10));

    [Display(Name = nameof(GraphicsStrings.MosaicEffect_Origin), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<RelativePoint> Origin { get; } = Property.CreateAnimatable(RelativePoint.Center);

    private static void BindRelativeOrigin(
        ShaderUniformWriter writer,
        Vector2 value,
        ShaderExecutionContext context)
    {
        RenderPlacement placement = context.OutputPlacement;
        PixelRect destinationDeviceBounds = placement.DeviceBounds;
        PixelRect completeDeviceBounds = placement.ToDevice(context.OutputBounds);
        writer.Set(new Vector2(
            completeDeviceBounds.X
            - destinationDeviceBounds.X
            + (value.X * completeDeviceBounds.Width),
            completeDeviceBounds.Y
            - destinationDeviceBounds.Y
            + (value.Y * completeDeviceBounds.Height)));
    }

    private static void BindAbsoluteOrigin(
        ShaderUniformWriter writer,
        Vector2 value,
        ShaderExecutionContext context)
    {
        var semanticOrigin = context.OutputBounds.Position - context.OutputPlacement.LogicalOrigin;
        writer.Set(new Vector2(
            (value.X + semanticOrigin.X) * context.OutputPlacement.Scale.Value,
            (value.Y + semanticOrigin.Y) * context.OutputPlacement.Scale.Value));
    }

    public new partial class Resource
    {
        protected override void ApplyTo(FilterEffectContext context)
        {
            // Animated values can overshoot the property range between valid keyframes.
            var logicalTileSize = new Size(
                float.IsFinite(TileSize.Width) ? MathF.Max(0.0001f, TileSize.Width) : 0.0001f,
                float.IsFinite(TileSize.Height) ? MathF.Max(0.0001f, TileSize.Height) : 0.0001f);
            var tileSize = new Vector2(logicalTileSize.Width, logicalTileSize.Height);
            var origin = new Vector2(Origin.Point.X, Origin.Point.Y);
            context.Shader(ShaderDescription.WholeSource(
                s_shaderSource,
                RenderBoundsContract.FullInput,
                bindings =>
                {
                    bindings.Uniform(
                        "tileSize",
                        tileSize,
                        static (writer, tile, execution) => writer.Set(tile * execution.OutputPlacement.Scale.Value));
                    bindings.Uniform("sourceRatio", Vector2.One, RasterShaderMapping.BindSourceRatio);
                    bindings.Uniform("sourceOffset", Vector2.Zero, RasterShaderMapping.BindSourceOffset);
                    if (Origin.Unit == RelativeUnit.Relative)
                    {
                        bindings.Uniform(
                            "origin",
                            origin,
                            BindRelativeOrigin);
                    }
                    else
                    {
                        bindings.Uniform(
                            "origin",
                            origin,
                            BindAbsoluteOrigin);
                    }
                },
                SKShaderTileMode.Clamp));
        }
    }
}
