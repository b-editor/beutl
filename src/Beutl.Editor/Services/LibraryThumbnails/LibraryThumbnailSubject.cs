using System.Text.Json.Nodes;
using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.Models;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Threading;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Beutl.Editor.Services.LibraryThumbnails;

/// <summary>
/// The project's own content the thumbnails show types on: the picture a filter effect applies to, and the sides
/// a transition goes between. Each picture keeps the size it has in the project, so an effect is applied to it at
/// that size and only the result is scaled down to the thumbnail. Each picture is decoded once for every thumbnail
/// drawn from it and kept until the subject is disposed.
/// </summary>
/// <remarks>
/// A thumbnail without a picture to show falls back to the samples: nothing was drawn where the content was taken.
/// </remarks>
internal sealed class LibraryThumbnailSubject : IDisposable
{
    // The longer side a picture is kept at, at most: four times a thumbnail's, so an effect that sharpens or
    // enlarges keeps detail.
    private const int MaxPictureSize = 1280;

    // The longer side the first look at an object is drawn at, to find where it is.
    private const int ProbeSize = 640;

    // The room a thumbnail leaves around an object, as a share of its larger side, so a shadow or an outline an
    // effect adds shows.
    private const float ObjectMargin = 0.12f;

    private const byte EmptyAlpha = 8;

    private static readonly ILogger s_logger = Log.CreateLogger<LibraryThumbnailSubject>();

    private LibraryThumbnailSubject(Picture? effectPicture, Size effectArea, Picture? from, Picture? to)
    {
        EffectPicture = effectPicture;
        EffectArea = effectArea;
        TransitionFrom = from;
        TransitionTo = to;
    }

    /// <summary>
    /// The picture a filter effect applies to, or <see langword="null"/> to use the sample. An object's picture is
    /// what it draws and nothing around it, so an effect that works from what it applies to, such as a clipping,
    /// sees the object's own bounds, as it does in the project.
    /// </summary>
    public Picture? EffectPicture { get; }

    /// <summary>
    /// What an effect's thumbnail frames, centred on <see cref="EffectPicture"/>, in the project's units: the
    /// project's frame for the frame the preview drew, and an object with room around it, so a shadow or an outline
    /// an effect adds shows.
    /// </summary>
    public Size EffectArea { get; }

    /// <summary>The side a transition goes from, or <see langword="null"/> when nothing is there.</summary>
    public Picture? TransitionFrom { get; }

    /// <summary>The side a transition goes to, or <see langword="null"/> when nothing is there.</summary>
    public Picture? TransitionTo { get; }

    /// <summary>Whether a transition has a side to show; otherwise its thumbnail uses the samples.</summary>
    public bool HasTransitionSides => TransitionFrom != null || TransitionTo != null;

    /// <summary>
    /// The frame the preview has drawn, of a scene whose frame is <paramref name="frameSize"/>: what the library
    /// applies effects to, and what its transitions bring in. Nothing is drawn again for it. Takes
    /// <paramref name="frame"/>.
    /// </summary>
    public static Task<LibraryThumbnailSubject?> FromFrameAsync(
        Ref<Bitmap>? frame, PixelSize frameSize, CancellationToken cancellationToken = default)
    {
        if (frame == null)
            return Task.FromResult<LibraryThumbnailSubject?>(null);

        // Started without the token: a task cancelled before it starts would never dispose the frame it took.
        return Task.Run(() =>
        {
            using (frame)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Picture? picture = TrySave(TryCopy(frame.Value), frameSize.ToSize(1), crop: null);
                // The frame comes in from nothing, which every transition shows, a slide included, and goes out the same
                // way played backwards.
                return (LibraryThumbnailSubject?)new LibraryThumbnailSubject(picture, frameSize.ToSize(1), from: null, to: picture);
            }
        });
    }

    /// <summary>
    /// <paramref name="element"/> on its own as it is at <paramref name="time"/>, cropped to what it draws: what a
    /// picker for one of its effects shows them on.
    /// </summary>
    /// <remarks>
    /// It reads its media through the readers the preview has open, so nothing is opened again for it.
    /// </remarks>
    public static async Task<LibraryThumbnailSubject?> CaptureElementAsync(
        Element element, MediaTime time, CancellationToken cancellationToken = default)
    {
        // The live element is read here, on the thread that edits it; the copy is drawn on the render thread.
        if (CopyAlone(element, time) is not { } copy)
            return null;

        return await RenderThread.Dispatcher.InvokeAsync(
            () =>
            {
                (Picture? Picture, Size Area) captured = CaptureObject(copy);
                return (LibraryThumbnailSubject?)new LibraryThumbnailSubject(captured.Picture, captured.Area, null, null);
            },
            DispatchPriority.Low,
            cancellationToken);
    }

    /// <summary>
    /// The two sides of <paramref name="edge"/> of <paramref name="element"/>, each on its own at the boundary:
    /// what a picker for the transition there shows transitions between. A side with no element is empty.
    /// </summary>
    public static async Task<LibraryThumbnailSubject?> CaptureEdgeAsync(
        Element element, ElementEdge edge, CancellationToken cancellationToken = default)
    {
        Element? outgoing = edge == ElementEdge.Start ? ElementTransitions.FindPrevious(element) : element;
        Element? incoming = edge == ElementEdge.Start ? element : ElementTransitions.FindNext(element);
        // The outgoing side shows its last moment and the incoming side its first.
        Copy? from = outgoing == null ? null : CopyAlone(outgoing, outgoing.Range.End - EdgeInset);
        Copy? to = incoming == null ? null : CopyAlone(incoming, incoming.Start.Value);

        (Picture? From, Picture? To) sides = await RenderThread.Dispatcher.InvokeAsync(
            () => (CaptureFrame(from), CaptureFrame(to)),
            DispatchPriority.Low,
            cancellationToken);
        return new LibraryThumbnailSubject(null, default, sides.From, sides.To);
    }

    // Just inside the end of an element, which no longer draws at its end.
    private static MediaTime EdgeInset => MediaTime.FromSeconds(1 / 120.0);

    public void Dispose()
    {
        EffectPicture?.Dispose();
        TransitionFrom?.Dispose();
        // The frame the preview drew is both the effect's picture and the side a transition brings in.
        if (!ReferenceEquals(TransitionTo, EffectPicture))
            TransitionTo?.Dispose();
    }

    // The whole frame of a copy, at most MaxPictureSize on its longer side.
    private static Picture? CaptureFrame(Copy? copy)
    {
        if (copy == null)
            return null;

        PixelSize frameSize = copy.Scene.FrameSize.Value;
        float scale = Math.Min(1f, MaxPictureSize / (float)Math.Max(frameSize.Width, frameSize.Height));
        return TrySave(TryRenderScene(copy, scale), frameSize.ToSize(1), crop: null);
    }

    // An object is first drawn small to find where it is, then, when that leaves it few pixels, again at a scale
    // that gives what it covers MaxPictureSize pixels, up to the project's own. Returns what it draws, and the area
    // its thumbnails frame around that.
    private static (Picture? Picture, Size Area) CaptureObject(Copy copy)
    {
        PixelSize frameSize = copy.Scene.FrameSize.Value;
        float longer = Math.Max(frameSize.Width, frameSize.Height);
        float probeScale = Math.Min(1f, ProbeSize / longer);
        Bitmap? probe = TryRenderScene(copy, probeScale);
        if (probe == null || FindCovered(probe) is not { } covered)
        {
            probe?.Dispose();
            return (null, default);
        }

        var content = new Rect(
            covered.X / probeScale, covered.Y / probeScale, covered.Width / probeScale, covered.Height / probeScale);
        float scale = Math.Clamp(MaxPictureSize / Math.Max(content.Width, content.Height), probeScale, 1f);
        Bitmap? bitmap = probe;
        if (scale > probeScale * 1.25f)
        {
            probe.Dispose();
            bitmap = TryRenderScene(copy, scale);
        }
        else
        {
            scale = probeScale;
        }

        // Found again at the scale drawn, so the crop follows the pixels rather than a rounded probe.
        PixelRect? area = bitmap == null ? null : FindCovered(bitmap);
        if (bitmap == null || area is not { } found)
        {
            bitmap?.Dispose();
            return (null, default);
        }

        var size = new Size(found.Width / scale, found.Height / scale);
        float margin = Math.Max(size.Width, size.Height) * ObjectMargin;
        return (TrySave(bitmap, size, found), new Size(size.Width + (2 * margin), size.Height + (2 * margin)));
    }

    // A copy of element alone in a scene of its scene's size, and the time to draw it at, inside the element. The
    // copy keeps the live element out of a second scene. It drops the element's transitions, which would fade it in or
    // out at the edge it is drawn at, and keeps its times, so an animation on the scene's clock reads the time the
    // project shows.
    private static Copy? CopyAlone(Element element, MediaTime time)
    {
        try
        {
            Scene? owner = element.FindHierarchicalParent<Scene>();
            PixelSize frameSize = owner?.FrameSize.Value ?? LibraryThumbnailSamples.FrameSize;
            if (frameSize.Width <= 0 || frameSize.Height <= 0)
                frameSize = LibraryThumbnailSamples.FrameSize;

            JsonObject json = CoreSerializer.SerializeToJsonObject(element);
            if (CoreSerializer.DeserializeFromJsonObject(json, typeof(Element)) is not Element copy)
                return null;

            copy.EnterTransition.Value = null;
            copy.ExitTransition.Value = null;
            MediaTime start = element.Start.Value;
            MediaTime end = element.Range.End;
            MediaTime at = time < start ? start : time;
            if (at >= end)
                at = end - start > EdgeInset ? end - EdgeInset : start;

            string directory = Path.Combine(Path.GetTempPath(), "beutl-library-thumbnail");
            var scene = new Scene(frameSize.Width, frameSize.Height, string.Empty)
            {
                Duration = { Value = end > MediaTime.Zero ? end : MediaTime.FromSeconds(1) },
                Uri = ObjectTemplateItem.ToFileUri(Path.Combine(directory, "subject.scene")),
            };
            copy.Uri = ObjectTemplateItem.ToFileUri(Path.Combine(directory, $"{copy.Id.Value}.belm"));
            scene.Children.Add(copy);
            return new Copy(scene, at);
        }
        catch (Exception ex)
        {
            s_logger.LogDebug(ex, "Could not copy an element for the library thumbnails.");
            return null;
        }
    }

    // Draws the copy at `scale` of its frame. Its media is read through the readers the preview shares, from
    // proxies where the preview uses them, rather than through files opened again.
    private static Bitmap? TryRenderScene(Copy copy, float scale)
    {
        try
        {
            using var renderer = new SceneRenderer(
                copy.Scene,
                RenderIntent.Preview,
                scale,
                disableResourceShare: false,
                maxWorkingScale: WorkingScaleCeiling.Preview(scale));
            renderer.CacheOptions = RenderCacheOptions.Disabled;
            renderer.Render(renderer.Compositor.EvaluateGraphics(copy.Time));
            using Bitmap snapshot = renderer.Snapshot();
            return snapshot.Convert(BitmapColorType.Bgra8888, BitmapAlphaType.Premul, BitmapColorSpace.Srgb);
        }
        catch (Exception ex)
        {
            s_logger.LogDebug(ex, "Could not draw the content for the library thumbnails.");
            return null;
        }
    }

    // An 8-bit sRGB copy of a frame the preview drew, at most MaxPictureSize on its longer side.
    private static Bitmap? TryCopy(Bitmap frame)
    {
        try
        {
            using Bitmap converted = frame.Convert(BitmapColorType.Bgra8888, BitmapAlphaType.Premul, BitmapColorSpace.Srgb);
            float scale = Math.Min(1f, MaxPictureSize / (float)Math.Max(converted.Width, converted.Height));
            if (scale >= 1f)
                return converted.Clone();

            var info = new SKImageInfo(
                Math.Max(1, (int)MathF.Round(converted.Width * scale)),
                Math.Max(1, (int)MathF.Round(converted.Height * scale)),
                SKColorType.Bgra8888,
                SKAlphaType.Premul,
                converted.SKBitmap.ColorSpace);
            SKBitmap? scaled = converted.SKBitmap.Resize(info, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
            return scaled == null ? null : new Bitmap(scaled);
        }
        catch (Exception ex)
        {
            s_logger.LogDebug(ex, "Could not copy the preview's frame for the library thumbnails.");
            return null;
        }
    }

    // Makes what was drawn the picture thumbnails draw, cropped when asked, or returns null when nothing was drawn.
    // `size` is what the pixels cover in the project's units. Takes the bitmap.
    //
    // An image source only opens files, so the pixels pass through one: written to a folder of its own that only the
    // user can open, decoded, and removed with the folder at once. The project's content never sits in the shared
    // temp folder, which on Linux is /tmp, and nothing is left behind.
    private static Picture? TrySave(Bitmap? bitmap, Size size, PixelRect? crop)
    {
        if (bitmap == null)
            return null;

        string? directory = null;
        try
        {
            if (FindCovered(bitmap) == null)
                return null;

            using Bitmap? cropped = crop is { } rect ? bitmap.ExtractSubset(rect) : null;
            Bitmap picture = cropped ?? bitmap;
            directory = Directory.CreateTempSubdirectory("beutl-library-thumbnail-").FullName;
            string path = Path.Combine(directory, "picture.png");
            // The fastest compression, a quarter of the time the default takes: the file is read only once.
            using (SKPixmap pixmap = picture.SKBitmap.PeekPixels())
            using (SKData? data = pixmap.Encode(new SKPngEncoderOptions(SKPngEncoderFilterFlags.AllFilters, 1)))
            {
                if (data == null)
                    return null;

                using FileStream file = new(path, FileMode.CreateNew, FileAccess.Write);
                data.SaveTo(file);
            }

            return Picture.TryOpen(path, new PixelSize(picture.Width, picture.Height), size);
        }
        catch (Exception ex)
        {
            s_logger.LogDebug(ex, "Could not save the content for the library thumbnails.");
            return null;
        }
        finally
        {
            bitmap.Dispose();
            if (directory != null)
                TryDeleteDirectory(directory);
        }
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException ex)
        {
            s_logger.LogDebug(ex, "Could not remove a picture's folder for the library thumbnails.");
        }
        catch (UnauthorizedAccessException ex)
        {
            s_logger.LogDebug(ex, "Could not remove a picture's folder for the library thumbnails.");
        }
    }

    // The smallest rectangle holding every pixel that is not clear, or null when all are.
    private static PixelRect? FindCovered(Bitmap bitmap)
    {
        int left = bitmap.Width, top = bitmap.Height, right = -1, bottom = -1;
        int bytesPerPixel = bitmap.BytesPerPixel;
        for (int y = 0; y < bitmap.Height; y++)
        {
            ReadOnlySpan<byte> row = bitmap.GetRow(y);
            for (int x = 0; x < bitmap.Width; x++)
            {
                if (row[(x * bytesPerPixel) + 3] <= EmptyAlpha)
                    continue;

                left = Math.Min(left, x);
                right = Math.Max(right, x);
                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y);
            }
        }

        return right < 0 ? null : new PixelRect(left, top, right - left + 1, bottom - top + 1);
    }

    /// <summary>
    /// Captured content, decoded once for every thumbnail drawn from it: its size in pixels, the size it covers in the
    /// project's units, at which a thumbnail draws it, and the image a thumbnail draws.
    /// </summary>
    internal sealed class Picture : IDisposable
    {
        // Holds the image decoded while the picture lives, so a thumbnail shares it; its file is gone by then.
        private readonly ImageSource.Resource _decoded;

        private Picture(PixelSize pixels, Size size, ImageSource source, ImageSource.Resource decoded)
        {
            Pixels = pixels;
            Size = size;
            Source = source;
            _decoded = decoded;
        }

        public PixelSize Pixels { get; }

        public Size Size { get; }

        /// <summary>
        /// The image, decoded. A renderer that shares resources draws it without opening its file, which is removed
        /// once it has been decoded.
        /// </summary>
        public ImageSource Source { get; }

        // The decoded pixels, for tests.
        internal Bitmap Image => _decoded.Bitmap!;

        // Decodes the file, or returns null when it cannot be read.
        public static Picture? TryOpen(string path, PixelSize pixels, Size size)
        {
            var source = ImageSource.Open(path);
            ImageSource.Resource decoded = source.ToResource(new CompositionContext(MediaTime.Zero));
            if (!decoded.IsOffline && decoded.Bitmap != null)
                return new Picture(pixels, size, source, decoded);

            decoded.Dispose();
            return null;
        }

        public void Dispose()
        {
            _decoded.Dispose();
        }
    }

    private sealed record Copy(Scene Scene, MediaTime Time);
}
