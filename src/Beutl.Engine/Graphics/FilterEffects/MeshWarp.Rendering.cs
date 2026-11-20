using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shaders;
using Beutl.Logging;
using Beutl.Media;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

public sealed partial class MeshWarp
{
    private static readonly ILogger s_logger = Log.CreateLogger(nameof(MeshWarp));

    private static bool HasArea(Rect bounds) => RenderRectValidation.IsFiniteNonNegative(bounds)
        && bounds.Width > 0 && bounds.Height > 0 && float.IsFinite(bounds.Right) && float.IsFinite(bounds.Bottom);

    private static Point MapPoint(Rect bounds, Point normalized)
        => new(bounds.X + (double)normalized.X * bounds.Width, bounds.Y + (double)normalized.Y * bounds.Height);

    private static Rect TransformBounds((Tessellation Mesh, BoundsCapture? Capture) data, Rect bounds)
    {
        if (bounds.IsInvalid) return bounds;
        if (!HasArea(bounds)) return Rect.Empty;

        var output = new Rect(MapPoint(bounds, data.Mesh.Minimum), MapPoint(bounds, data.Mesh.Maximum));
        return HasArea(output) ? output : Rect.Empty;
    }

    private readonly record struct BoundsCapture(MeshWarp Owner, MediaTime Time);

    private static void CaptureOnly(BoundsCapture capture, CustomFilterEffectContext context)
    {
        if (context is { Intent: RenderIntent.Preview, Purpose: RenderRequestPurpose.Frame })
            capture.Owner.CaptureInputBounds(capture.Time, context.Targets.CalculateCompleteBounds());
    }

    private static void Apply((Tessellation Mesh, BoundsCapture? Capture) data, CustomFilterEffectContext context)
    {
        Rect sharedBounds = context.Targets.CalculateCompleteBounds();
        if (context is { Intent: RenderIntent.Preview, Purpose: RenderRequestPurpose.Frame }
            && data.Capture is { } capture)
        {
            capture.Owner.CaptureInputBounds(capture.Time, sharedBounds);
        }

        bool sharedSpace = context.Targets.Count > 1 && HasArea(sharedBounds);
        for (int i = 0; i < context.Targets.Count; i++)
        {
            EffectTarget target = context.Targets[i];
            if (!target.IsMaterialized) continue;

            Tessellation? tileMesh = data.Mesh;
            Rect warpBounds = target.Bounds;
            if (sharedSpace)
            {
                var subRect = new Rect(
                    (target.Bounds.X - sharedBounds.X) / sharedBounds.Width,
                    (target.Bounds.Y - sharedBounds.Y) / sharedBounds.Height,
                    target.Bounds.Width / sharedBounds.Width,
                    target.Bounds.Height / sharedBounds.Height);
                tileMesh = data.Mesh.CreateSubMesh(subRect);
                warpBounds = sharedBounds;
            }

            if (tileMesh is null)
            {
                context.Targets.RemoveAt(i--);
                continue;
            }

            Rect bounds = new(MapPoint(warpBounds, tileMesh.Minimum), MapPoint(warpBounds, tileMesh.Maximum));
            if (!HasArea(bounds))
            {
                context.Targets.RemoveAt(i--);
                continue;
            }

            // The renderer clips these local bounds after mapping them into the composition's target domain.
            EffectTarget output = context.CreateTarget(bounds);
            try
            {
                if (!output.IsEmpty && Draw(tileMesh, warpBounds, context, target, output))
                    context.Targets[i] = output;
            }
            finally
            {
                if (output.Owner is null)
                    output.Dispose();
            }
        }
    }

    private static bool Draw(
        Tessellation mesh,
        Rect warpBounds,
        CustomFilterEffectContext context,
        EffectTarget target,
        EffectTarget output)
    {
        RenderTarget source = target.RenderTarget!;
        source.PrepareForSampling(
            RenderTargetSamplingIntent.SameContextTextureSampling(output.RenderTarget!.RawValue.Context));
        using SurfaceSnapshot.Lease snapshot = SurfaceSnapshot.Take(source.Value);

        RenderPlacement src = target.Placement;
        RenderPlacement dst = output.Placement;
        float srcScale = src.Scale.Value;
        float dstScale = dst.Scale.Value;
        SKSamplingOptions sampling = RasterShaderMapping.SamplingFor(srcScale, dstScale);
        using SKShader? shader = CreateSourceShader(
            snapshot.Image, source.RawValue.Context, target.Bounds, src, sampling, out Rect srcRasterBounds);
        if (shader is null)
        {
            CustomFilterEffectContext.ThrowIfDeliveryReadbackFailure(context.Intent, src.DeviceBounds);
            context.RenderTargetLeaseSession?.MarkContentDropped();
            s_logger.LogWarning("Mesh Warp could not sample the source surface; the preview keeps the source pixels.");
            return false;
        }

        var texs = new SKPoint[mesh.Rest.Length];
        var positions = new SKPoint[mesh.Rest.Length];
        Point srcOrigin = target.Bounds.Position - srcRasterBounds.Position;
        Point dstOrigin = warpBounds.Position - dst.RasterBounds.Position;
        for (int i = 0; i < positions.Length; i++)
        {
            texs[i] = ToDevice(mesh.Rest[i], target.Bounds.Size, srcOrigin, srcScale);
            positions[i] = ToDevice(mesh.Warped[i], warpBounds.Size, dstOrigin, dstScale);
        }

        using SKVertices vertices = SKVertices.CreateCopy(SKVertexMode.Triangles, positions, texs, colors: null, mesh.Indices);
        using var paint = new SKPaint { Shader = shader, IsAntialias = true };
        using ImmediateCanvas canvas = context.Open(output);
        using (canvas.PushDeviceSpace())
        {
            canvas.Clear();
            canvas.Canvas.DrawVertices(vertices, SKBlendMode.Modulate, paint);
        }
        return true;

        static SKPoint ToDevice(Point point, Size size, Point origin, float scale)
            => new((float)((origin.X + (double)point.X * size.Width) * scale),
                (float)((origin.Y + (double)point.Y * size.Height) * scale));
    }

    // Subset to the semantic source footprint when the backing buffer includes an apron so Clamp never bleeds apron pixels.
    private static SKShader? CreateSourceShader(
        SKImage? image,
        GRRecordingContext? recordingContext,
        Rect sourceBounds,
        RenderPlacement src,
        SKSamplingOptions sampling,
        out Rect rasterBounds)
    {
        rasterBounds = src.RasterBounds;
        if (image is null) return null;

        float srcScale = src.Scale.Value;
        var imageBounds = new PixelRect(new PixelSize(image.Width, image.Height));
        (PixelRect semanticDeviceBounds, Rect semanticRasterBounds) = RasterShaderMapping.ResolveSemanticSource(
            sourceBounds, srcScale, src.DeviceBounds, src.RasterBounds);
        var semanticSubset = new PixelRect(
            semanticDeviceBounds.X - src.DeviceBounds.X,
            semanticDeviceBounds.Y - src.DeviceBounds.Y,
            semanticDeviceBounds.Width,
            semanticDeviceBounds.Height);

        if (semanticSubset != imageBounds
            && semanticSubset.Width > 0 && semanticSubset.Height > 0
            && imageBounds.Contains(semanticSubset))
        {
            using SKImage? subset = recordingContext is null
                ? image.Subset(semanticSubset.ToSKRectI())
                : image.Subset(recordingContext, semanticSubset.ToSKRectI());
            if (subset is not null)
            {
                rasterBounds = semanticRasterBounds;
                return subset.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, sampling);
            }
        }

        return image.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, sampling);
    }

    private sealed class Tessellation(
        MeshWarpGrid grid,
        float strength,
        MeshWarpInterpolation interpolation,
        int count,
        int indexCount)
    {
        public Point[] Rest { get; } = new Point[count];
        public Point[] Warped { get; } = new Point[count];
        public ushort[] Indices { get; } = new ushort[indexCount];
        public Point Minimum { get; private set; }
        public Point Maximum { get; private set; }

        public Tessellation? CreateSubMesh(Rect normalizedSubRect)
            => Create(grid, strength, interpolation, normalizedSubRect);

        public static Tessellation? Create(
            MeshWarpGrid grid,
            float strength,
            MeshWarpInterpolation interpolation,
            Rect? normalizedSubRect = null)
        {
            int subdivisions = 8;
            while ((grid.Rows * subdivisions + 1) * (grid.Columns * subdivisions + 1) > ushort.MaxValue)
                subdivisions--;

            int rows = grid.Rows * subdivisions;
            int columns = grid.Columns * subdivisions;
            int stride = columns + 1;
            var result = new Tessellation(grid, strength, interpolation, (rows + 1) * stride, rows * columns * 6);
            float left = float.PositiveInfinity, top = float.PositiveInfinity;
            float right = float.NegativeInfinity, bottom = float.NegativeInfinity;
            int nextIndex = 0;

            for (int row = 0; row <= rows; row++)
            {
                for (int column = 0; column <= columns; column++)
                {
                    int vertex = row * stride + column;
                    var tileRest = new Point(column / (float)columns, row / (float)rows);
                    Point sharedRest = normalizedSubRect is { } sub
                        ? new Point(sub.X + tileRest.X * sub.Width, sub.Y + tileRest.Y * sub.Height)
                        : tileRest;
                    Point warped = MeshWarpGrid.Lerp(sharedRest, grid.Evaluate(sharedRest, interpolation), strength);
                    if (!MeshWarpGrid.IsFinite(warped)) return null;

                    result.Rest[vertex] = tileRest;
                    result.Warped[vertex] = warped;
                    left = MathF.Min(left, warped.X);
                    top = MathF.Min(top, warped.Y);
                    right = MathF.Max(right, warped.X);
                    bottom = MathF.Max(bottom, warped.Y);

                    if (row < rows && column < columns)
                    {
                        result.Indices[nextIndex++] = (ushort)vertex;
                        result.Indices[nextIndex++] = (ushort)(vertex + 1);
                        result.Indices[nextIndex++] = (ushort)(vertex + stride);
                        result.Indices[nextIndex++] = (ushort)(vertex + 1);
                        result.Indices[nextIndex++] = (ushort)(vertex + stride + 1);
                        result.Indices[nextIndex++] = (ushort)(vertex + stride);
                    }
                }
            }

            result.Minimum = new Point(left, top);
            result.Maximum = new Point(right, bottom);
            return result;
        }
    }

    public new partial class Resource
    {
        private Tessellation? _tessellation;
        private int _tessellationVersion = -1;

        protected override void ApplyTo(FilterEffectContext context)
        {
            BoundsCapture? capture = null;
            if (context.IsPreviewFrame && GetOriginal() is MeshWarp owner)
            {
                MediaTime time = EvaluationContext?.DisplayedTime ?? MediaTime.Zero;
                if (!owner.CaptureInputBounds(time, context.Bounds))
                    capture = new BoundsCapture(owner, time);
            }

            if (!float.IsFinite(Strength) || Strength == 0 || Mesh is null || Mesh.IsIdentity())
            {
                if (capture is { } pendingCapture)
                    context.CustomEffect(pendingCapture, CaptureOnly, static (_, r) => r);
                return;
            }

            if (_tessellationVersion != Version)
            {
                MeshWarpGrid grid = Mesh.Resample(Rows, Columns, Interpolation);
                _tessellation = grid.IsIdentity() ? null : Tessellation.Create(grid, Strength / 100f, Interpolation);
                _tessellationVersion = Version;
            }

            if (_tessellation is { } mesh)
                context.CustomEffect((mesh, capture), Apply, TransformBounds);
            else if (capture is { } pendingCapture)
                context.CustomEffect(pendingCapture, CaptureOnly, static (_, r) => r);
        }
    }
}
