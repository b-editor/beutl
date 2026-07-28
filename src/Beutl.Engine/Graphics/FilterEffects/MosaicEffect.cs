using System.ComponentModel.DataAnnotations;
using System.Numerics;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.MosaicEffect), ResourceType = typeof(GraphicsStrings))]
public partial class MosaicEffect : FilterEffect
{
    private const string ShaderSource =
        """
        uniform shader src;
        uniform float2 origin;
        uniform float2 tileSize;

        half4 main(float2 fragCoord) {
            float2 blockIndex = floor((fragCoord - origin) / tileSize);
            float2 sampleCoord = (blockIndex * tileSize + tileSize * 0.5) + origin;
            return src.eval(sampleCoord);
        }
        """;

    public MosaicEffect()
    {
        ScanProperties<MosaicEffect>();
    }

    [Range(typeof(Size), "0.0001, 0.0001", "max,max")]
    [Display(Name = nameof(GraphicsStrings.MosaicEffect_TileSize), ResourceType = typeof(GraphicsStrings))]
    public IProperty<Size> TileSize { get; } = Property.CreateAnimatable(new Size(10, 10));

    [Display(Name = nameof(GraphicsStrings.MosaicEffect_Origin), ResourceType = typeof(GraphicsStrings))]
    public IProperty<RelativePoint> Origin { get; } = Property.CreateAnimatable(RelativePoint.Center);

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        var tileSize = new Vector2(r.TileSize.Width, r.TileSize.Height);
        var origin = new Vector2(r.Origin.Point.X, r.Origin.Point.Y);
        context.Shader(ShaderDescription.WholeSource(
            ShaderSource,
            RenderBoundsContract.FullInput,
            bindings =>
            {
                bindings.Uniform(
                    "tileSize",
                    tileSize,
                    BindScaledVector,
                    structuralKey: (typeof(MosaicEffect), "tile-size"),
                    runtimeIdentity: new RenderRuntimeIdentity("MosaicEffect.tile-size"));
                if (r.Origin.Unit == RelativeUnit.Relative)
                {
                    bindings.Uniform(
                        "origin",
                        origin,
                        BindRelativeOrigin,
                        structuralKey: (typeof(MosaicEffect), RelativeUnit.Relative),
                        runtimeIdentity: new RenderRuntimeIdentity(
                            ("MosaicEffect.origin", RelativeUnit.Relative)));
                }
                else
                {
                    bindings.Uniform(
                        "origin",
                        origin,
                        BindAbsoluteOrigin,
                        structuralKey: (typeof(MosaicEffect), RelativeUnit.Absolute),
                        runtimeIdentity: new RenderRuntimeIdentity(
                            ("MosaicEffect.origin", RelativeUnit.Absolute)));
                }
            },
            SKShaderTileMode.Clamp));
    }

    private static void BindScaledVector(
        ShaderUniformWriter writer,
        Vector2 value,
        ShaderExecutionContext context)
        => writer.Set(value * context.WorkingScale);

    private static void BindRelativeOrigin(
        ShaderUniformWriter writer,
        Vector2 value,
        ShaderExecutionContext context)
    {
        PixelRect completeDeviceBounds = PixelRect.FromRect(context.OutputBounds, context.WorkingScale);
        writer.Set(new Vector2(
            completeDeviceBounds.X - context.DeviceBounds.X + value.X * completeDeviceBounds.Width,
            completeDeviceBounds.Y - context.DeviceBounds.Y + value.Y * completeDeviceBounds.Height));
    }

    private static void BindAbsoluteOrigin(
        ShaderUniformWriter writer,
        Vector2 value,
        ShaderExecutionContext context)
    {
        var semanticOrigin = context.OutputBounds.Position - context.LogicalOrigin;
        writer.Set(new Vector2(
            (value.X + semanticOrigin.X) * context.WorkingScale,
            (value.Y + semanticOrigin.Y) * context.WorkingScale));
    }
}
