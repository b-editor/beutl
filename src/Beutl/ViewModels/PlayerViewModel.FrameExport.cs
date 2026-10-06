using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Models;

namespace Beutl.ViewModels;

public partial class PlayerViewModel
{
    public async Task<Rect> StartSelectRect()
    {
        TcsForCrop = new TaskCompletionSource<Rect>();
        IsCropMode.Value = true;
        Rect r = await TcsForCrop.Task;
        TcsForCrop = null;
        return r;
    }

    public TaskCompletionSource<Rect>? TcsForCrop { get; private set; }

    /// <summary>
    /// Measures the logical pixel size <paramref name="drawable"/> renders into at unit scale.
    /// </summary>
    public async Task<PixelSize> MeasureSelectedDrawable(Drawable drawable)
    {
        await Pause();

        return await RenderThread.Dispatcher.InvokeAsync(() =>
        {
            if (Scene == null) throw new Exception("Scene is null.");
            SceneRenderer sceneRenderer = EditViewModel.Renderer.Value;
            PixelSize frameSize = sceneRenderer.FrameSize;
            CompositionContext compositionContext = CreateSelectedDrawableCompositionContext(
                CurrentFrame.Value,
                frameSize);
            using var resource = drawable.ToResource(compositionContext);
            using var root = new DrawableRenderNode(resource);
            using (var context = new GraphicsContext2D(root, frameSize.ToSize(1)))
            {
                drawable.Render(context, resource);
            }

            var request = new RenderNodeRenderRequest
            {
                Intent = RenderIntent.Preview,
                TargetDomain = compositionContext.TargetDomain,
                OutputScale = 1,
                CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
            };
            using var renderer = new RenderNodeRenderer(root, request);
            RenderNodeRenderRequest measureRequest = request with
            {
                TargetDomain = ResolveSelectedDrawableDomain(
                    renderer,
                    request,
                    compositionContext.TargetDomain),
            };
            return PixelRect.FromRect(GetSelectedDrawableRasterRegion(renderer.Measure(measureRequest))).Size;
        });
    }

    /// <summary>
    /// Renders <paramref name="drawable"/> on its own at the given <paramref name="outputScale"/>.
    /// </summary>
    public async Task<Bitmap> DrawSelectedDrawable(Drawable drawable, float outputScale = 1f)
    {
        await Pause();

        return await RenderThread.Dispatcher.InvokeAsync(() =>
        {
            if (Scene == null) throw new Exception("Scene is null.");
            SceneRenderer sceneRenderer = EditViewModel.Renderer.Value;
            PixelSize frameSize = sceneRenderer.FrameSize;
            CompositionContext compositionContext = CreateSelectedDrawableCompositionContext(
                CurrentFrame.Value,
                frameSize);
            using var resource = drawable.ToResource(compositionContext);
            using var root = new DrawableRenderNode(resource);
            using (var context = new GraphicsContext2D(root, frameSize.ToSize(1), outputScale))
            {
                drawable.Render(context, resource);
            }

            var request = new RenderNodeRenderRequest
            {
                Intent = RenderIntent.Delivery,
                TargetDomain = compositionContext.TargetDomain,
                OutputScale = outputScale,
                MaxWorkingScale = WorkingScaleCeiling.Export(),
                CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
            };
            using var renderer = new RenderNodeRenderer(root, request);
            RenderNodeRenderRequest exportRequest = request with
            {
                TargetDomain = ResolveSelectedDrawableDomain(
                    renderer,
                    request,
                    compositionContext.TargetDomain),
            };
            Rect outputBounds = GetSelectedDrawableRasterRegion(renderer.Measure(exportRequest));
            using RenderNodeRasterization rasterization = renderer.Rasterize(exportRequest with
            {
                RequestedRegion = outputBounds,
            });
            return rasterization.Bitmap?.Clone()
                ?? throw new InvalidOperationException("The selected drawable produced no raster output.");
        });
    }

    internal static Rect GetSelectedDrawableRasterRegion(RenderNodeMeasurement measurement)
        => measurement.OutputBounds;

    /// <summary>
    /// The target domain a selected-drawable export renders against: wide enough to own a fragment that
    /// resolves its region from the target, but never narrower than the drawable itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A request's target domain is a hard output clip, so exporting against the scene frame cropped an
    /// element hanging over the edge and produced nothing at all for one entirely outside it - neither of
    /// which is what "save this element as an image" means. Measuring without a domain first asks the
    /// drawable how much room it actually takes.
    /// </para>
    /// <para>
    /// A subtree that reads the whole target - a backdrop, say - cannot answer that question: it says so by
    /// throwing, because the extent it would report is the domain it was not given. Keeping only the frame
    /// there reinstates the crop this method exists to avoid, and a backdrop carried past the frame edge by
    /// a transform loses the export entirely. Ask again with the frame and read
    /// <see cref="RenderNodeMeasurement.QueryBounds"/> instead: the domain clips
    /// <see cref="RenderNodeMeasurement.OutputBounds"/> but not the region the subtree asked to read, so
    /// that is the content extent, and it stays finite - an unbounded full-target read contributes to the
    /// output it fills, never to what it queries.
    /// </para>
    /// </remarks>
    internal static Rect? ResolveSelectedDrawableDomain(
        RenderNodeRenderer renderer,
        RenderNodeRenderRequest request,
        Rect? frameDomain)
    {
        try
        {
            Rect measured = renderer.Measure(request with { TargetDomain = null }).OutputBounds;
            if (measured.Width <= 0 || measured.Height <= 0)
                return frameDomain;

            return frameDomain is { } frame ? frame.Union(measured) : measured;
        }
        catch (RenderTargetDomainRequiredException)
        {
            if (frameDomain is not { } frame)
                return null;

            Rect queried = renderer.Measure(request with { TargetDomain = frame }).QueryBounds;
            return queried.IsInvalid || queried.IsEmpty ? frame : frame.Union(queried);
        }
    }

    internal static CompositionContext CreateSelectedDrawableCompositionContext(
        TimeSpan frame,
        PixelSize frameSize)
        => new(frame)
        {
            TargetDomain = new Rect(default, frameSize.ToSize(1)),
        };

    /// <summary>
    /// Renders the current frame at full scale on a throwaway renderer, ignoring preview quality.
    /// </summary>
    public Task<Bitmap> DrawFrameAtFullScale() => DrawFrameAtScale(1f);

    /// <summary>
    /// Renders the current frame at <paramref name="outputScale"/> on a throwaway renderer,
    /// ignoring preview quality. The surface is <c>ceil(FrameSize * outputScale)</c>.
    /// </summary>
    public async Task<Bitmap> DrawFrameAtScale(float outputScale)
    {
        await Pause();

        return await RenderThread.Dispatcher.InvokeAsync(() =>
        {
            if (Scene == null) throw new Exception("Scene is null.");

            // This renderer forces original sources, so unlike preview it cannot fall back to a
            // proxy when the original is missing; without this check the render resource path
            // swallows the open failure and the user saves a blank frame with no error. Match the
            // export preflight before spending the render.
            IReadOnlySet<string> referencedSources =
                Beutl.Editor.ExportSourceValidator.CollectRenderableSources(Scene, CurrentFrame.Value);
            IReadOnlyList<string> missingSources =
                Beutl.Editor.ExportSourceValidator.GetMissingPaths(referencedSources);
            if (missingSources.Count > 0)
            {
                throw new InvalidOperationException(string.Format(
                    CultureInfo.CurrentCulture,
                    Language.MessageStrings.SaveFrameMissingSourceFile,
                    missingSources[0],
                    missingSources.Count));
            }

            using var renderer = ExportRendererFactory.Create(Scene, outputScale);

            var compositionFrame = renderer.Compositor.EvaluateGraphics(CurrentFrame.Value);
            renderer.Render(compositionFrame);

            // Surface is ceil(FrameSize * outputScale); return as-is.
            return renderer.Snapshot();
        });
    }
}
