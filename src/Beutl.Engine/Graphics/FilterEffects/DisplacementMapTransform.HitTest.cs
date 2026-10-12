using System.Numerics;
using System.Runtime.InteropServices;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

public abstract partial class DisplacementMapTransform
{
    /// <summary>Declares a hit test that resolves a coordinate the way the shader fallback resamples one.</summary>
    /// <remarks>
    /// The stage reads <c>src</c> at a point displaced by the map, so the map is sampled here as the stage samples it.
    /// A tile brush would rasterize a render target, so those stages keep the forwarded query and its defect.
    /// </remarks>
    private protected static (
        RenderHitTestContract? Contract,
        IReadOnlyList<RenderResourceBinding>? Resources) DeclareSampling(
        Brush.Resource displacementMap,
        RenderResource<Brush.Resource> map,
        DrawableMapTransformKind kind,
        Vector2 vector,
        float angle,
        Vector2 center,
        GradientSpreadMethod spreadMethod,
        DisplacementMapChannel channel,
        bool signed)
    {
        if (ResolvePresentedBrush(displacementMap) is TileBrush.Resource)
            return default;

        RenderHitTestContract contract = RenderHitTestContract.Custom(
            new DisplacementSampling(
                kind,
                vector,
                angle,
                center,
                spreadMethod.ToSKShaderTileMode(),
                channel,
                signed),
            static (state, context, point) => state.HitTest(context, point));
        return (contract, [s_hitTestMapSlot.Bind(map)]);
    }

    /// <summary>Answers a hit test the way a fallback entry point resolves the coordinate it samples.</summary>
    private readonly record struct DisplacementSampling(
        DrawableMapTransformKind Kind,
        Vector2 Vector,
        float Angle,
        Vector2 Center,
        SKShaderTileMode SourceTileMode,
        DisplacementMapChannel Channel,
        bool Signed)
    {
        public bool HitTest(RenderHitTestContext context, Point point)
        {
            Rect outputBounds = context.OutputBounds;

            // The mapping names a source for any coordinate, but the entry point runs only for fragments of
            // this stage's own output. Outside that rectangle the stage evaluated nothing, and
            // RenderHitTestContract.Custom applies no such gate of its own.
            if (!outputBounds.ContainsExclusive(point))
                return false;

            DisplacementMapChannel channel = Channel;
            bool signed = Signed;
            float displacement = context.UseResource(
                s_hitTestMapSlot,
                map => ResolveDisplacement(map, outputBounds, point, channel, signed));
            Point source = ResolveSource(point, outputBounds, displacement);

            IReadOnlyList<RenderHitTestInput> inputs = context.Inputs;
            for (int index = 0; index < inputs.Count; index++)
            {
                RenderHitTestInput input = inputs[index];
                if (TryTileIntoBounds(source, input.Bounds, SourceTileMode, out Point tiled) && input.HitTest(tiled))
                    return true;
            }

            return false;
        }

        /// <remarks>
        /// The uniform binders express the same quantities in the shader's device coordinates. A hit test is
        /// asked in logical coordinates and must answer the same at every scale, so the translation is taken
        /// unscaled and the pivot is rebuilt from the output rectangle rather than from the device grid.
        /// </remarks>
        private Point ResolveSource(Point point, Rect outputBounds, float displacement)
        {
            if (Kind == DrawableMapTransformKind.Translate)
                return point + new Vector(Vector.X * displacement, Vector.Y * displacement);

            Point pivot = outputBounds.Center + new Vector(Center.X, Center.Y);
            float x = point.X - pivot.X;
            float y = point.Y - pivot.Y;
            if (Kind == DrawableMapTransformKind.Scale)
            {
                float scaleX = MathF.Max(float.Lerp(1f, Vector.X, displacement), 0.001f);
                float scaleY = MathF.Max(float.Lerp(1f, Vector.Y, displacement), 0.001f);
                return new Point((x / scaleX) + pivot.X, (y / scaleY) + pivot.Y);
            }

            float theta = Angle * displacement;
            float cos = MathF.Cos(theta);
            float sin = MathF.Sin(theta);
            return new Point(((x * cos) - (y * sin)) + pivot.X, ((x * sin) + (y * cos)) + pivot.Y);
        }
    }

    /// <summary>Reads the displacement the entry point reads at <paramref name="point"/>.</summary>
    /// <remarks>
    /// A map that produces no shader is the transparent one <see cref="CreateDisplacementMapShader"/> installs
    /// in its place, which displaces by nothing at all - or, signed, by the whole negative extent.
    /// </remarks>
    private static float ResolveDisplacement(
        Brush.Resource map,
        Rect outputBounds,
        Point point,
        DisplacementMapChannel channel,
        bool signed)
    {
        Vector4 color = SampleMap(map, outputBounds, point);
        float value = channel switch
        {
            DisplacementMapChannel.Alpha => color.W,
            // RGB samples are already premultiplied, so alpha must not be applied again.
            DisplacementMapChannel.Luminance =>
                ((0.2126f * color.X) + (0.7152f * color.Y) + (0.0722f * color.Z)),
            DisplacementMapChannel.Red => color.X,
            DisplacementMapChannel.Green => color.Y,
            _ => color.Z,
        };

        return signed ? (value * 2f) - 1f : value;
    }

    /// <summary>
    /// Evaluates the map's own shader at one logical point, in the premultiplied working colour space the
    /// entry point receives it in.
    /// </summary>
    /// <remarks>
    /// <see cref="CreateDisplacementMapShader"/> lays the brush out over a rectangle the size of the output
    /// and offsets it onto the output's position, so a logical point addresses the brush at its distance from
    /// that position. The render intent reaches only the tile-brush path, which
    /// <see cref="DeclareSampling"/> has already excluded.
    /// </remarks>
    private static Vector4 SampleMap(Brush.Resource map, Rect outputBounds, Point point)
    {
        using SKShader? shader = new BrushConstructor(
                new Rect(outputBounds.Size),
                map,
                BlendMode.SrcOver,
                RenderIntent.Preview,
                drawableBrushMaterializer: null)
            .CreateShader();
        if (shader is null)
            return default;

        var info = new SKImageInfo(1, 1, SKColorType.RgbaF16, SKAlphaType.Premul, s_workingColorSpace);
        using var bitmap = new SKBitmap(info);
        using var canvas = new SKCanvas(bitmap);
        using var paint = new SKPaint { Shader = shader, BlendMode = SKBlendMode.Src, IsAntialias = false };
        float x = point.X - outputBounds.X;
        float y = point.Y - outputBounds.Y;

        // The single pixel's centre, not its corner, is where the shader is read.
        canvas.Translate(0.5f - x, 0.5f - y);
        canvas.DrawRect(new SKRect(x - 0.5f, y - 0.5f, x + 0.5f, y + 0.5f), paint);

        ReadOnlySpan<Half> pixel = MemoryMarshal.Cast<byte, Half>(bitmap.GetPixelSpan());
        return new Vector4((float)pixel[0], (float)pixel[1], (float)pixel[2], (float)pixel[3]);
    }

    /// <summary>Moves a source coordinate onto the input the way the sampler's tile mode does.</summary>
    /// <remarks>
    /// The stage samples with the user's spread method, so only <see cref="SKShaderTileMode.Decal"/> leaves an outside
    /// point empty. Coverage is bottom-right exclusive, so every mode ends on the last float below the edge.
    /// </remarks>
    private static bool TryTileIntoBounds(Point point, Rect bounds, SKShaderTileMode mode, out Point tiled)
    {
        tiled = default;
        float maxX = float.BitDecrement(bounds.Right);
        float maxY = float.BitDecrement(bounds.Bottom);
        if (maxX < bounds.Left || maxY < bounds.Top)
            return false;

        if (mode == SKShaderTileMode.Decal
            && !bounds.ContainsExclusive(point))
        {
            return false;
        }

        tiled = new Point(
            TileCoordinate(point.X, bounds.Left, bounds.Width, maxX, mode),
            TileCoordinate(point.Y, bounds.Top, bounds.Height, maxY, mode));
        return true;
    }

    private static float TileCoordinate(float value, float origin, float extent, float max, SKShaderTileMode mode)
    {
        float offset = value - origin;
        if (mode is SKShaderTileMode.Repeat or SKShaderTileMode.Mirror && extent > 0)
        {
            float period = mode == SKShaderTileMode.Mirror ? extent * 2f : extent;
            offset -= period * MathF.Floor(offset / period);
            if (mode == SKShaderTileMode.Mirror && offset > extent)
                offset = period - offset;
        }

        return Math.Clamp(origin + offset, origin, max);
    }
}
