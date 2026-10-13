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
    // the texture size on AMD Radeon and Apple's Metal, so the centre of the pixel starting there is sampled.
    // The snap is in this stage's device grid; an input at another density can still land on a corner (#2723).
    private const string ShaderSource =
        """
        uniform shader src;
        uniform float2 origin;
        uniform float2 tileSize;

        half4 main(float2 fragCoord) {
            float2 blockIndex = floor((fragCoord - origin) / tileSize);
            float2 sampleCoord = (blockIndex * tileSize + tileSize * 0.5) + origin;
            return src.eval(floor(sampleCoord) + 0.5);
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
    public IProperty<Size> TileSize { get; } = Property.CreateAnimatable(new Size(10, 10));

    [Display(Name = nameof(GraphicsStrings.MosaicEffect_Origin), ResourceType = typeof(GraphicsStrings))]
    public IProperty<RelativePoint> Origin { get; } = Property.CreateAnimatable(RelativePoint.Center);

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        // Animated values can overshoot the property range between valid keyframes.
        var logicalTileSize = new Size(
            float.IsFinite(r.TileSize.Width) ? MathF.Max(0.0001f, r.TileSize.Width) : 0.0001f,
            float.IsFinite(r.TileSize.Height) ? MathF.Max(0.0001f, r.TileSize.Height) : 0.0001f);
        var tileSize = new Vector2(logicalTileSize.Width, logicalTileSize.Height);
        var origin = new Vector2(r.Origin.Point.X, r.Origin.Point.Y);
        context.Shader(ShaderDescription.WholeSource(
            s_shaderSource,
            RenderBoundsContract.FullInput,
            bindings =>
            {
                bindings.Uniform(
                    "tileSize",
                    tileSize,
                    static (writer, tile, execution) => writer.Set(tile * execution.WorkingScale));
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
                new MosaicSampling(logicalTileSize, r.Origin),
                static (state, context, point) => state.HitTest(context, point))));
    }

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
    /// A pixel carries what the input covered at its tile's centre, so forwarding the query would disagree both
    /// ways. The output rectangle bounds the answer, since <see cref="RenderHitTestContract.Custom"/> applies none.
    /// </remarks>
    private readonly record struct MosaicSampling(Size TileSize, RelativePoint Origin)
    {
        public bool HitTest(RenderHitTestContext context, Point point)
        {
            Rect outputBounds = context.OutputBounds;

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
        /// The stage samples with <see cref="SKShaderTileMode.Clamp"/>, so a centre outside the input reads its edge.
        /// Coverage is bottom-right exclusive, so the clamp ends on the last float below the edge.
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
