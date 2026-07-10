using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.MosaicEffect), ResourceType = typeof(GraphicsStrings))]
public partial class MosaicEffect : FilterEffect
{
    private const string ShaderSource =
        """
        uniform shader src;
        uniform float2 origin;
        uniform float2 tileSize;
        uniform float2 resolution;

        half4 main(float2 fragCoord) {
            float2 blockIndex = floor((fragCoord - origin) / tileSize);

            // タイルの中心位置を求める
            float2 sampleCoord = (blockIndex * tileSize + tileSize * 0.5) + origin;

            // The legacy custom effect sampled the source with Clamp tiling; the fused whole-source src child is
            // Decal, so clamp the tile-centre sample into the buffer to reproduce edge-tile colours.
            sampleCoord = clamp(sampleCoord, float2(0.0), resolution - 0.5);

            // 中心位置の色をサンプリングして返す
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

    public override void Describe(EffectGraphBuilder builder, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        Size tileSize = r.TileSize;
        RelativePoint originPoint = r.Origin;
        // builder.WorkingScale is already clamped to the 16384 px/axis buffer budget for the node's bounds
        // (PlanFilterEffectRenderNode.Process clamps before describing), and this render-time pass always bakes at full
        // input bounds, so the per-pass re-clamp in ResolveResources resolves the same w — these describe-time
        // uniforms match the execute-time buffer even at the clamp edge (pinned by MosaicEffect_AtBufferBudgetEdge).
        float w = builder.WorkingScale;
        (int bufW, int bufH) = RenderNodeContext.DeviceBufferSize(builder.Bounds, w);
        Point origin = originPoint.Unit == RelativeUnit.Relative
            ? originPoint.ToPixels(new Size(bufW, bufH))
            : originPoint.Point * w;

        // The tile grid (origin/tileSize/resolution) is authored in the FULL-frame device space, so the pass MUST
        // bake at full input bounds: a RenderTime contract keeps it full-frame even when a downstream deflating pass
        // (a fixed Clipping) would otherwise ROI-crop it to a sub-rect and shift/clip the grid (review M2). Identity
        // bounds would forgo nothing here — the shader samples non-local tile centres, so an ROI crop is never sound.
        builder.Shader(ShaderNodeDescriptor.WholeSource(
            ShaderSource,
            BoundsContract.RenderTime,
            u => u.Float2("origin", (float)origin.X, (float)origin.Y)
                  .Float2("tileSize", tileSize.Width * w, tileSize.Height * w)
                  .Float2("resolution", bufW, bufH)));
    }
}
