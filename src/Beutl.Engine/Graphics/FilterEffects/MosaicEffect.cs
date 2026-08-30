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
                    BindScaledVector);
                if (r.Origin.Unit == RelativeUnit.Relative)
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
            SKShaderTileMode.Clamp,
            hitTest: RenderHitTestContract.Custom(
                new MosaicSampling(r.TileSize, r.Origin),
                static (state, context, point) => state.HitTest(context, point))));
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
        Rect outputBounds = context.OutputBounds;
        Point logicalOrigin = context.LogicalOrigin;
        PixelRect destinationDeviceBounds = context.DeviceBounds;
        var deviceGridOffset = new Vector(
            (destinationDeviceBounds.X / context.WorkingScale) - logicalOrigin.X,
            (destinationDeviceBounds.Y / context.WorkingScale) - logicalOrigin.Y);
        PixelRect completeDeviceBounds = PixelRect.FromRect(
            outputBounds.Translate(deviceGridOffset),
            context.WorkingScale);
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
        var semanticOrigin = context.OutputBounds.Position - context.LogicalOrigin;
        writer.Set(new Vector2(
            (value.X + semanticOrigin.X) * context.WorkingScale,
            (value.Y + semanticOrigin.Y) * context.WorkingScale));
    }

    /// <summary>Answers a hit test the way the entry point resolves a fragment: at its tile's centre.</summary>
    /// <remarks>
    /// <para>
    /// The entry point discards <c>fragCoord</c> and returns <c>src</c> at the centre of the tile holding it,
    /// so what a pixel carries is what the input covered at that centre and nothing about what it covered at
    /// the pixel. Both directions of that are reachable with one ordinary shape: a tile straddling an edge
    /// paints its whole area wherever its centre is covered, and erases its whole area wherever the centre is
    /// not. Forwarding the query unchanged answers for the input's coverage instead, which disagrees on both.
    /// </para>
    /// <para>
    /// Unlike a stage that only sometimes relocates content, this one has no setting that stops it: the grid
    /// exists at every tile size. The contract is therefore unconditional, and it costs no precision to be so -
    /// it restates the entry point's own mapping rather than widening anything to the output rectangle.
    /// </para>
    /// <para>
    /// The output rectangle still bounds the answer, as a floor rather than as a claim: the entry point is
    /// evaluated only for fragments of it, so outside it the stage wrote nothing whatever the grid maps a
    /// coordinate to. That bound is stated here because <see cref="RenderHitTestContract.Custom"/> applies
    /// none, and a stage that resolves a coordinate instead of forwarding one - clamping, wrapping, folding -
    /// has nothing else to stop it answering for the whole plane.
    /// </para>
    /// </remarks>
    private readonly record struct MosaicSampling(Size TileSize, RelativePoint Origin)
    {
        public bool HitTest(RenderHitTestContext context, Point point)
        {
            Rect outputBounds = context.OutputBounds;

            // The grid is defined over the whole plane and names a sample for any coordinate, but the entry
            // point runs only for fragments of this stage's own output. Outside that rectangle the stage
            // evaluated nothing, so there is no sample to clamp and nothing to report - and
            // RenderHitTestContract.Custom applies no such gate of its own.
            if (!outputBounds.ContainsExclusive(point))
                return false;

            Point origin = ResolveOrigin(outputBounds);
            var sample = new Point(
                SampleCoordinate(point.X, origin.X, TileSize.Width),
                SampleCoordinate(point.Y, origin.Y, TileSize.Height));

            IReadOnlyList<RenderHitTestInput> inputs = context.Inputs;
            for (int index = 0; index < inputs.Count; index++)
            {
                RenderHitTestInput input = inputs[index];
                if (TryClampToBounds(sample, input.Bounds, out Point clamped) && input.HitTest(clamped))
                    return true;
            }

            return false;
        }

        /// <remarks>
        /// The uniform binders express the same origin in the shader's device coordinates, where it is snapped
        /// to whole composition-device pixels. A hit test is asked in logical coordinates and must answer the
        /// same at every scale, so it is resolved here without that snap; the two can therefore disagree
        /// within one device pixel of a tile edge, which is where the tile a point belongs to is ambiguous
        /// anyway.
        /// </remarks>
        private Point ResolveOrigin(Rect outputBounds)
            => outputBounds.Position + (Origin.Unit == RelativeUnit.Relative
                ? new Vector(
                    Origin.Point.X * outputBounds.Width,
                    Origin.Point.Y * outputBounds.Height)
                : new Vector(Origin.Point.X, Origin.Point.Y));

        private static float SampleCoordinate(float value, float origin, float tile)
            => (MathF.Floor((value - origin) / tile) * tile) + (tile * 0.5f) + origin;

        /// <summary>Moves a sample onto the input the way the sampler's clamp does, if the input has one.</summary>
        /// <remarks>
        /// <para>
        /// The stage samples with <see cref="SKShaderTileMode.Clamp"/>, so a tile whose centre falls outside
        /// the input reads the input's edge rather than transparency, and the pixels of that tile are painted
        /// with whatever the edge carries.
        /// </para>
        /// <para>
        /// Where the edge is depends on the engine's rule for what a content node covers, which is bottom-right
        /// exclusive: <see cref="Rect.ContainsExclusive"/> is what
        /// <see cref="Rendering.RectangleRenderNode"/> and the image and video source nodes answer over, and it
        /// is the logical form of a device rectangle spanning half-open pixel columns. Clamping to
        /// <see cref="Rect.Right"/> itself would therefore land on the one coordinate the input rejects, which
        /// is why the target is the last <see langword="float"/> below it rather than the edge or an epsilon
        /// short of it. An input that has no such coordinate on an axis covers no column there and carries
        /// nothing for the tile to read.
        /// </para>
        /// </remarks>
        private static bool TryClampToBounds(Point point, Rect bounds, out Point clamped)
        {
            float maxX = float.BitDecrement(bounds.Right);
            float maxY = float.BitDecrement(bounds.Bottom);
            if (maxX < bounds.Left || maxY < bounds.Top)
            {
                clamped = default;
                return false;
            }

            clamped = new Point(
                Math.Clamp(point.X, bounds.Left, maxX),
                Math.Clamp(point.Y, bounds.Top, maxY));
            return true;
        }
    }
}
