using Beutl.Composition;
using Beutl.Editor.Services;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Models;
using Beutl.ProjectSystem;

namespace Beutl.ViewModels;

public partial class PlayerViewModel
{
    /// <summary>
    /// Measures the logical pixel size <paramref name="drawable"/> renders into at unit scale.
    /// </summary>
    public Task<PixelSize> MeasureSelectedDrawable(Drawable drawable)
        => RenderSelectedDrawable(
            drawable,
            RenderIntent.Preview,
            outputScale: 1,
            maxWorkingScale: float.PositiveInfinity,
            (renderer, request) => PixelRect.FromRect(renderer.Measure(request).OutputBounds).Size);

    /// <summary>
    /// Renders <paramref name="drawable"/> on its own at the given <paramref name="outputScale"/>.
    /// </summary>
    public Task<Bitmap> DrawSelectedDrawable(Drawable drawable, float outputScale = 1f)
        => RenderSelectedDrawable(
            drawable,
            RenderIntent.Delivery,
            outputScale,
            WorkingScaleCeiling.Export(),
            (renderer, request) =>
            {
                Rect outputBounds = renderer.Measure(request).OutputBounds;
                using RenderNodeRasterization rasterization = renderer.Rasterize(request with
                {
                    RequestedRegion = outputBounds,
                });
                return rasterization.Bitmap?.Clone()
                    ?? throw new InvalidOperationException("The selected drawable produced no raster output.");
            });

    // Records drawable on its own on the render thread and passes the renderer and the request, with the
    // domain resolved for the drawable, to finish.
    private async Task<T> RenderSelectedDrawable<T>(
        Drawable drawable,
        RenderIntent intent,
        float outputScale,
        float maxWorkingScale,
        Func<RenderNodeRenderer, RenderNodeRenderRequest, T> finish)
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
                resource.Render(context);
            }

            var request = new RenderNodeRenderRequest
            {
                Intent = intent,
                TargetDomain = compositionContext.TargetDomain,
                OutputScale = outputScale,
                MaxWorkingScale = maxWorkingScale,
                CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
            };
            using var renderer = new RenderNodeRenderer(root, request);
            return finish(renderer, request with
            {
                TargetDomain = ResolveSelectedDrawableDomain(
                    renderer,
                    request,
                    compositionContext.TargetDomain),
            });
        });
    }

    /// <summary>
    /// The target domain a selected-drawable export renders against: wide enough to own a fragment that
    /// resolves its region from the target, but never narrower than the drawable itself.
    /// </summary>
    /// <remarks>
    /// The domain clips the output, so the drawable is measured without one first. A subtree that reads the
    /// whole target throws then, so it is measured with the frame and its QueryBounds are used instead.
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
        MediaTime frame,
        PixelSize frameSize)
        => new(frame)
        {
            TargetDomain = new Rect(default, frameSize.ToSize(1)),
        };

    /// <summary>
    /// Renders the frame the start page shows for this scene on the preview renderer, without the editor's overlays:
    /// the frame at the playhead, or the middle of the scene's elements when nothing shows there, as after playing
    /// through to the end. Returns <see langword="null"/> when nothing shows at either.
    /// </summary>
    internal async Task<Bitmap?> DrawThumbnailFrame()
    {
        await Pause();

        return await RenderThread.Dispatcher.InvokeAsync(() =>
        {
            if (Scene is not { } scene
                || EditViewModel.Renderer.Value is not { IsDisposed: false, IsGraphicsRendering: false } renderer)
                return null;

            MediaTime playhead = CurrentFrame.Value;
            if (DrawVisibleFrame(renderer, playhead) is { } frame)
                return frame;

            if (scene.Children.Count == 0)
                return null;

            MediaTime start = scene.Children.Min(element => element.Range.Start);
            MediaTime end = scene.Children.Max(element => element.Range.End);
            frame = DrawVisibleFrame(renderer, start + new MediaTime((end - start).Flicks / 2));
            // Selection bounds and later queries must use the playhead's geometry if the close is refused.
            renderer.UpdateFrame(renderer.Compositor.EvaluateGraphics(playhead));
            return frame;
        });
    }

    private static Bitmap? DrawVisibleFrame(SceneRenderer renderer, MediaTime time)
    {
        renderer.Render(renderer.Compositor.EvaluateGraphics(time));
        using Bitmap snapshot = renderer.SnapshotWithoutGizmos() ?? renderer.Snapshot();
        Bitmap frame = snapshot.Convert(BitmapColorType.Bgra8888, BitmapAlphaType.Premul, BitmapColorSpace.Srgb);
        if (!ObjectTemplatePreviewRenderer.IsBlank(frame))
            return frame;

        frame.Dispose();
        return null;
    }

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

            return renderer.Snapshot();
        });
    }
}
