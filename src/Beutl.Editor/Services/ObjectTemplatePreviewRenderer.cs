using System.Text.Json.Nodes;
using Beutl.Audio;
using Beutl.Audio.Effects;
using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Logging;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor.Services;

/// <summary>
/// Renders the PNG preview embedded in a saved <see cref="ObjectTemplateItem"/>.
/// </summary>
public static class ObjectTemplatePreviewRenderer
{
    public const int PreviewWidth = 256;
    public const int PreviewHeight = 144;

    private const int MaxPreviewBytes = 256 * 1024;
    private const float MaxPreviewScale = 64f;
    private const byte BlankAlphaThreshold = 8;

    // Matches Scene's own default, so a template with no scene to read composes the way the
    // project it was most likely authored in would have.
    private static readonly PixelSize DefaultFrameSize = new(1920, 1080);

    private static readonly ILogger s_logger = Log.CreateLogger(typeof(ObjectTemplatePreviewRenderer));

    /// <summary>
    /// Renders a preview of <paramref name="instance"/>, or returns null when it has nothing to show.
    /// </summary>
    /// <remarks>
    /// Pass the live instance: for a non-<see cref="Drawable"/> the preview is the owning
    /// <see cref="Drawable"/> found through the hierarchy, which a detached copy cannot reach.
    /// The live object is only ever read; anything that has to be re-parented is cloned first.
    /// </remarks>
    public static async ValueTask<byte[]?> RenderPngAsync(
        ICoreSerializable instance, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);

        try
        {
            byte[]? png = await RenderThread.Dispatcher.InvokeAsync(
                () => Render(instance), ct: cancellationToken).ConfigureAwait(false);

            if (png is { Length: > MaxPreviewBytes })
            {
                s_logger.LogDebug("Template preview is {Size} bytes; not embedding it.", png.Length);
                return null;
            }

            return png;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A template must still be saveable when it cannot be drawn (missing sources, no GPU
            // for a GLSL effect, a plugin type that throws while composing).
            s_logger.LogDebug(ex, "Failed to render a preview for {Type}.", instance.GetType());
            return null;
        }
    }

    private static byte[]? Render(ICoreSerializable instance)
    {
        return instance switch
        {
            Element element => RenderElement(element),
            Sound or AudioEffect => null,
            Drawable drawable => RenderDrawable(drawable),
            _ => RenderNonDrawable(instance)
        };
    }

    private static byte[]? RenderElement(Element element)
    {
        // The element belongs to the edited scene, so the throwaway scene gets a clone. Start is
        // rebased because SceneCompositor only collects elements whose Range contains the time.
        if (Clone(element, typeof(Element)) is not Element copy)
            return null;

        copy.Start = TimeSpan.Zero;
        TimeSpan time = copy.Length > TimeSpan.Zero
            ? copy.Length / 2
            : TimeSpan.Zero;

        // The element's sizes are authored against its own scene's frame — a caption is a 355pt
        // glyph in a 1920x1080 project — so the preview scene has to keep that frame for layout to
        // resolve the way it did there.
        PixelSize frameSize = ResolveFrameSize(element);

        // Scene.Children_CollectionChanged relates each element's path to the scene's own, so both
        // need a Uri even though this scene is never written. The paths are synthetic; nothing on
        // disk is read or created.
        string directory = Path.Combine(Path.GetTempPath(), "beutl-template-preview");
        var scene = new Scene(frameSize.Width, frameSize.Height, string.Empty)
        {
            Duration = copy.Length > TimeSpan.Zero ? copy.Length : TimeSpan.FromSeconds(1),
            Uri = ObjectTemplateItem.ToFileUri(Path.Combine(directory, "preview.scene"))
        };
        copy.Uri = ObjectTemplateItem.ToFileUri(Path.Combine(directory, $"{copy.Id}.belm"));
        scene.Children.Add(copy);

        // The compositor, not a hand-rolled walk, is what resolves the flow operators an element
        // may carry (DrawableGroup / DrawableDecorator populate their children from the flow).
        // Its resources are owned by it, so nothing here disposes them.
        using var compositor = new SceneCompositor(scene) { DisableResourceShare = true };
        CompositionFrame frame = compositor.EvaluateGraphics(time + scene.Start);

        return RenderResources(
            [.. frame.Objects.OfType<Drawable.Resource>()],
            frameSize.ToSize(1));
    }

    // Read from the live element: the clone is detached and can no longer reach its scene. A
    // template loaded from disk has no scene either, so it falls back to the default project frame.
    private static PixelSize ResolveFrameSize(Element element)
    {
        PixelSize frameSize = element.FindHierarchicalParent<Scene>()?.FrameSize ?? default;
        return frameSize.Width > 0 && frameSize.Height > 0 ? frameSize : DefaultFrameSize;
    }

    private static byte[]? RenderNonDrawable(ICoreSerializable instance)
    {
        if (instance is IHierarchical hierarchical
            && hierarchical.FindHierarchicalParent<Drawable>() is { } owner)
        {
            return RenderDrawable(owner);
        }

        return BuildSampleShape(instance) is { } sample ? RenderDrawable(sample) : null;
    }

    private static byte[]? RenderDrawable(Drawable drawable)
    {
        using var resource = drawable.ToResource(CompositionContext.Default);
        return RenderResources([resource], AvailableSize);
    }

    /// <summary>
    /// Draws <paramref name="resources"/> cropped to what they actually cover, scaled to fill the
    /// preview.
    /// </summary>
    /// <remarks>
    /// Fitting the drawn content rather than <paramref name="availableSize"/> is what keeps a
    /// thumbnail legible: scaling a whole 1920x1080 frame down would leave a caption a few pixels
    /// tall. <paramref name="availableSize"/> still has to be the authored frame, because that is
    /// what alignment resolves against. The resources belong to the caller.
    /// </remarks>
    private static byte[]? RenderResources(IReadOnlyList<Drawable.Resource> resources, Size availableSize)
    {
        if (resources.Count == 0)
            return null;

        Rect bounds = MeasureBounds(resources, availableSize);
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return null;

        float scale = Math.Clamp(
            MathF.Min(PreviewWidth / bounds.Width, PreviewHeight / bounds.Height),
            float.Epsilon,
            MaxPreviewScale);
        if (!float.IsFinite(scale) || scale <= 0f)
            scale = 1f;

        PixelRect rect = PixelRect.FromRect(bounds, scale);
        if (rect.Width <= 0 || rect.Height <= 0)
            return null;

        using RenderTarget? target = RenderTarget.Create(rect.Width, rect.Height);
        if (target == null)
            return null;

        using var canvas = new ImmediateCanvas(target, scale, scale * 2f, logicalSize: bounds.Size);
        canvas.Clear();
        using (canvas.PushTransform(Matrix.CreateTranslation(-bounds.X, -bounds.Y)))
        {
            foreach (Drawable.Resource resource in resources)
            {
                using var root = new DrawableRenderNode(resource);
                using (var context = new GraphicsContext2D(root, availableSize, scale))
                {
                    resource.GetOriginal().Render(context, resource);
                }

                root.PrepareForProcess(canvas);
                new RenderNodeProcessor(
                        root, useRenderCache: false, outputScale: scale, maxWorkingScale: scale * 2f)
                    .Render(canvas);
            }
        }

        using Bitmap snapshot = target.Snapshot();
        return EncodePng(snapshot);
    }

    private static Rect MeasureBounds(IReadOnlyList<Drawable.Resource> resources, Size availableSize)
    {
        var bounds = Rect.Empty;
        foreach (Drawable.Resource resource in resources)
        {
            using var root = new DrawableRenderNode(resource);
            using (var context = new GraphicsContext2D(root, availableSize))
            {
                resource.GetOriginal().Render(context, resource);
            }

            var processor = new RenderNodeProcessor(root, useRenderCache: false);
            RenderNodeOperation[] operations = processor.PullToRoot();
            try
            {
                foreach (RenderNodeOperation op in operations)
                {
                    bounds = bounds.Union(op.Bounds);
                }
            }
            finally
            {
                foreach (RenderNodeOperation op in operations)
                {
                    op.Dispose();
                }
            }
        }

        return bounds;
    }

    private static Size AvailableSize => new(PreviewWidth, PreviewHeight);

    // Assigning the live object to a fresh shape would tear it out of the edited scene's hierarchy,
    // so the sample shape only ever receives a detached copy.
    private static Drawable? BuildSampleShape(ICoreSerializable instance)
    {
        switch (instance)
        {
            case FilterEffect:
                if (Clone(instance, typeof(FilterEffect)) is not FilterEffect effect) return null;
                Shape subject = CreateSubject();
                subject.FilterEffect.CurrentValue = effect;
                return subject;

            case Transform:
                if (Clone(instance, typeof(Transform)) is not Transform transform) return null;
                Shape transformed = CreateSubject();
                transformed.Transform.CurrentValue = transform;
                return transformed;

            case Brush:
                if (Clone(instance, typeof(Brush)) is not Brush brush) return null;
                return new RectShape
                {
                    Width = { CurrentValue = PreviewWidth },
                    Height = { CurrentValue = PreviewHeight },
                    Fill = { CurrentValue = brush }
                };

            case Pen:
                if (Clone(instance, typeof(Pen)) is not Pen pen) return null;
                return new RectShape
                {
                    Width = { CurrentValue = PreviewWidth },
                    Height = { CurrentValue = PreviewHeight },
                    Fill = { CurrentValue = null },
                    Pen = { CurrentValue = pen }
                };

            case Geometry:
                if (Clone(instance, typeof(Geometry)) is not Geometry geometry) return null;
                return new GeometryShape { Data = { CurrentValue = geometry } };

            default:
                return null;
        }
    }

    // A plain fill would hide what a blur or a displacement does, so the subject carries both a
    // gradient and a hard edge.
    private static Shape CreateSubject()
    {
        var gradient = new LinearGradientBrush
        {
            StartPoint = { CurrentValue = RelativePoint.TopLeft },
            EndPoint = { CurrentValue = RelativePoint.BottomRight }
        };
        gradient.GradientStops.Add(new GradientStop(Color.FromRgb(0x4F, 0x8A, 0xF7), 0f));
        gradient.GradientStops.Add(new GradientStop(Color.FromRgb(0xF7, 0x8A, 0x4F), 1f));

        return new RectShape
        {
            Width = { CurrentValue = PreviewWidth * 0.75f },
            Height = { CurrentValue = PreviewHeight * 0.75f },
            Fill = { CurrentValue = gradient },
            Pen =
            {
                CurrentValue = new Pen
                {
                    Brush = { CurrentValue = new SolidColorBrush(Colors.White) },
                    Thickness = { CurrentValue = 6f }
                }
            }
        };
    }

    private static object? Clone(ICoreSerializable source, Type baseType)
    {
        JsonObject json = CoreSerializer.SerializeToJsonObject(source);
        return CoreSerializer.DeserializeFromJsonObject(json, baseType);
    }

    private static byte[]? EncodePng(Bitmap bitmap)
    {
        using Bitmap converted = bitmap.Convert(
            BitmapColorType.Bgra8888, BitmapAlphaType.Premul, BitmapColorSpace.Srgb);
        if (IsBlank(converted))
            return null;

        using var stream = new MemoryStream();
        return converted.Save(stream, EncodedImageFormat.Png) ? stream.ToArray() : null;
    }

    // An audio-only element composites to a fully transparent frame; embedding that would replace a
    // meaningful type icon with an empty square. A uniformly coloured but opaque result (a solid
    // brush swatch) is a legitimate preview and must survive this check.
    private static bool IsBlank(Bitmap bitmap)
    {
        if (bitmap.Width <= 0 || bitmap.Height <= 0)
            return true;

        int bytesPerPixel = bitmap.BytesPerPixel;
        if (bytesPerPixel < 4)
            return false;

        for (int y = 0; y < bitmap.Height; y++)
        {
            ReadOnlySpan<byte> row = bitmap.GetRow(y);
            for (int x = 0; x < bitmap.Width; x++)
            {
                if (row[(x * bytesPerPixel) + 3] > BlankAlphaThreshold)
                    return false;
            }
        }

        return true;
    }
}
