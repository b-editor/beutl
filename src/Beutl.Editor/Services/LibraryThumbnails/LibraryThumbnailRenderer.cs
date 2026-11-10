using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Transformation;
using Beutl.Graphics.Transitions;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.Models;
using Beutl.ProjectSystem;
using Beutl.Threading;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor.Services.LibraryThumbnails;

/// <summary>
/// How a thumbnail's frames are laid out in the sheet they are drawn into and kept as, and how fast they play.
/// </summary>
/// <param name="FrameCount">How many frames the thumbnail plays before it starts over.</param>
/// <param name="StillIndex">The frame shown while the thumbnail is not playing.</param>
internal sealed record LibraryThumbnailLayout(int FrameCount, int StillIndex)
{
    // A tile shows a frame at half this size on a display of twice the density, and at its full size on others.
    public static readonly PixelSize FrameSize = new(320, 180);

    public static readonly Rational FrameRate = new(24, 1);

    // Eight columns keep a sheet within 2560 pixels a side, which any GPU draws as one texture.
    public const int Columns = 8;

    public int Rows => (FrameCount + Columns - 1) / Columns;

    public PixelSize SheetSize => new(FrameSize.Width * Columns, FrameSize.Height * Rows);

    public TimeSpan FrameDuration => TimeSpan.FromSeconds(FrameRate.Denominator / (double)FrameRate.Numerator);

    public PixelRect GetFrameRect(int index)
    {
        return new PixelRect(
            index % Columns * FrameSize.Width,
            index / Columns * FrameSize.Height,
            FrameSize.Width,
            FrameSize.Height);
    }
}

/// <summary>A drawn thumbnail: its frames in one sheet, and how they are laid out and play.</summary>
/// <param name="Bitmap">
/// Every frame, laid out as <see cref="Layout"/> says, or only the frame shown at rest when that alone was drawn.
/// </param>
internal sealed record LibraryThumbnailSheet(Bitmap Bitmap, LibraryThumbnailLayout Layout) : IDisposable
{
    public void Dispose() => Bitmap.Dispose();
}

/// <summary>
/// Draws the moving thumbnails of filter effects and transitions: an effect, with its
/// <see cref="LibraryThumbnailRecipes">recipe</see>, over the project's content or the sample picture, and a transition
/// between the project's content or the sample clips.
/// </summary>
/// <remarks>
/// Content keeps the size it has in the project: an effect applies to it at that size, in the units a project
/// gives its values in, and only the result is scaled down to the thumbnail.
/// </remarks>
internal static class LibraryThumbnailRenderer
{
    // An effect's thumbnail lasts this long. A transition's goes out and comes back in, each side showing for a hold
    // in between, so it loops without a jump.
    internal static readonly MediaTime EffectLength = MediaTime.FromSeconds(2);
    internal static readonly MediaTime TransitionHold = MediaTime.FromSeconds(0.25);
    internal static readonly MediaTime TransitionLength = MediaTime.FromSeconds(1);

    private static readonly ILogger s_logger = Log.CreateLogger(typeof(LibraryThumbnailRenderer));

    public static bool CanRender(Type type)
    {
        return type is { IsAbstract: false, ContainsGenericParameters: false }
               && (type.IsAssignableTo(typeof(FilterEffect)) || type.IsAssignableTo(typeof(ClipTransition)))
               && type.GetConstructor(Type.EmptyTypes) != null;
    }

    // Whether the types a picker lists under baseType have thumbnails.
    public static bool HasThumbnails(Type baseType)
    {
        return baseType.IsAssignableTo(typeof(FilterEffect)) || baseType.IsAssignableTo(typeof(ClipTransition));
    }

    // A transition rests a third of the way through going out, where a dip or a fade still shows the side it hides by
    // half way.
    public static LibraryThumbnailLayout? GetLayout(Type type)
    {
        if (!CanRender(type))
            return null;

        if (type.IsAssignableTo(typeof(ClipTransition)))
        {
            int hold = ToFrames(TransitionHold);
            int length = ToFrames(TransitionLength);
            return new LibraryThumbnailLayout(2 * (hold + length), hold + (int)Math.Round(length * 0.35));
        }

        int count = ToFrames(EffectLength);
        return new LibraryThumbnailLayout(count, count / 2);
    }

    /// <summary>
    /// The smallest frame of 16:9, in whole steps of 16 by 9 units, that holds <paramref name="area"/>: content shows
    /// in it at its own size, centred.
    /// </summary>
    internal static PixelSize FrameAround(Size area)
    {
        double step = Math.Max(area.Width / 16, area.Height / 9);
        int units = Math.Max(1, (int)Math.Ceiling(step - 1e-4));
        return new PixelSize(16 * units, 9 * units);
    }

    /// <summary>
    /// Draws every frame of <paramref name="type"/>'s thumbnail on the samples into one sheet, or returns
    /// <see langword="null"/> when the type cannot be drawn.
    /// </summary>
    public static Task<LibraryThumbnailSheet?> RenderAsync(Type type, CancellationToken cancellationToken = default)
    {
        return RenderAsync(type, null, stillOnly: false, cancellationToken);
    }

    /// <summary>
    /// Draws <paramref name="type"/>'s thumbnail on <paramref name="subject"/>, or on the samples where it has
    /// nothing to show. With <paramref name="stillOnly"/>, only the frame shown at rest is drawn.
    /// </summary>
    /// <remarks>
    /// Each frame is drawn on its own at low priority on the render thread, so the preview's frames go first.
    /// </remarks>
    public static async Task<LibraryThumbnailSheet?> RenderAsync(
        Type type, LibraryThumbnailSubject? subject, bool stillOnly, CancellationToken cancellationToken = default)
    {
        if (GetLayout(type) is not { } layout)
            return null;

        Session? session = await RenderThread.Dispatcher.InvokeAsync(
            () => Session.TryCreate(type, layout, subject, stillOnly), DispatchPriority.Low, cancellationToken);
        if (session == null)
            return null;

        try
        {
            if (stillOnly)
            {
                bool drawn = await RenderThread.Dispatcher.InvokeAsync(
                    () => session.TryRenderFrame(layout.StillIndex, new PixelRect(LibraryThumbnailLayout.FrameSize)),
                    DispatchPriority.Low,
                    cancellationToken);
                return drawn && session.TakeSheet() is { } still ? new LibraryThumbnailSheet(still, layout) : null;
            }

            for (int i = 0; i < layout.FrameCount; i++)
            {
                int index = i;
                bool drawn = await RenderThread.Dispatcher.InvokeAsync(
                    () => session.TryRenderFrame(index, layout.GetFrameRect(index)), DispatchPriority.Low, cancellationToken);
                if (!drawn)
                    return null;
            }

            return session.TakeSheet() is { } sheet ? new LibraryThumbnailSheet(sheet, layout) : null;
        }
        finally
        {
            RenderThread.Dispatcher.Dispatch(session.Dispose, DispatchPriority.Low);
        }
    }

    // Gives one object to a property without making it the property owner's child, so several scenes can hold it.
    // Thumbnails are never saved, so it is never written out.
    private sealed class SharedValueExpression<T>(T value) : IExpression<T>
    {
        public string ExpressionString => string.Empty;

        public T Evaluate(ExpressionContext context) => value;

        public bool Validate(out string? error)
        {
            error = null;
            return true;
        }
    }

    private static int ToFrames(MediaTime time)
    {
        return (int)Math.Round(time.ToSeconds() * LibraryThumbnailLayout.FrameRate.ToDouble());
    }

    private static Scene CreateEffectScene(Type type, LibraryThumbnailSubject? subject)
    {
        var effect = (FilterEffect)Activator.CreateInstance(type)!;
        // The effect goes on a group around the picture, so a recipe can move the picture as a whole.
        DrawableGroup target;
        PixelSize frame;
        Size content;
        if (subject?.EffectPicture is { } picture)
        {
            frame = FrameAround(subject.EffectArea);
            content = picture.Size;
            target = new DrawableGroup();
            target.Children.Add(CreateImage(picture));
        }
        else
        {
            frame = LibraryThumbnailSamples.FrameSize;
            content = frame.ToSize(1);
            target = LibraryThumbnailSamples.CreateEffectSubject();
        }

        target.FilterEffect.Value = effect;
        LibraryThumbnailRecipes.Find(type)?.Apply(effect, new LibraryThumbnailContext(target, EffectLength, content));

        return CreateScene(
            frame,
            EffectLength,
            CreateElement(LibraryThumbnailSamples.CreateBackdrop(frame), MediaTime.Zero, EffectLength, 0),
            CreateElement(target, MediaTime.Zero, EffectLength, 1));
    }

    // Out, in and out again. Two sides go back and forth: the first goes out to the other by the transition, which
    // shows and goes out the same way back to the first. The way out is the first side's exit transition and the way
    // back the returning side's enter transition, so no element transitions at both ends, which would halve both
    // transitions. Returns the time of the scene each frame of the thumbnail shows, or null for frame by frame.
    private static (Scene Scene, Func<int, MediaTime>? TimeOf) CreateTransitionScene(
        Type type, LibraryThumbnailSubject? subject)
    {
        MediaTime loop = TransitionHold + TransitionLength + TransitionHold + TransitionLength;
        if (subject is { HasTransitionSides: true } && (subject.TransitionFrom == null || subject.TransitionTo == null))
            return CreateOneSideScene(type, subject, loop);

        Func<Drawable> createFirst, createOther;
        PixelSize frame;
        if (subject is { TransitionFrom: { } from, TransitionTo: { } to })
        {
            frame = FrameAround(from.Size);
            createFirst = () => CreateImage(from);
            createOther = () => CreateImage(to);
        }
        else
        {
            frame = LibraryThumbnailSamples.FrameSize;
            createFirst = () => LibraryThumbnailSamples.CreateClip(incoming: false);
            createOther = () => LibraryThumbnailSamples.CreateClip(incoming: true);
        }

        Element outgoing = CreateElement(createFirst(), MediaTime.Zero, TransitionHold + TransitionLength, 1);
        outgoing.ExitTransition.Value = CreateTransition(type, loop, frame);
        Element returning = CreateElement(createFirst(), loop - TransitionLength, TransitionLength, 1);
        returning.EnterTransition.Value = CreateTransition(type, loop, frame);
        Scene scene = CreateScene(
            frame,
            loop,
            CreateElement(LibraryThumbnailSamples.CreateBackdrop(frame), MediaTime.Zero, loop, 0),
            outgoing,
            CreateElement(createOther(), TransitionHold + TransitionLength, TransitionHold, 1),
            returning);
        return (scene, null);
    }

    // A side alone is drawn going the one way it does in the project, in from nothing at an element's start or out to
    // nothing at its end, and that is played backwards for the other way. Going out to nothing by a slide, which moves
    // the incoming side over a still outgoing one, the side would otherwise only vanish.
    private static (Scene Scene, Func<int, MediaTime>? TimeOf) CreateOneSideScene(
        Type type, LibraryThumbnailSubject subject, MediaTime loop)
    {
        bool entering = subject.TransitionFrom == null;
        LibraryThumbnailSubject.Picture side = (subject.TransitionFrom ?? subject.TransitionTo)!;
        PixelSize frame = FrameAround(side.Size);
        // The side goes its way over [start, start + length): before it, nothing or the side shows, and after it the
        // other of the two.
        MediaTime start = TransitionHold;
        MediaTime after = start + TransitionLength;
        MediaTime duration = after + TransitionHold;
        Element element = entering
            ? CreateElement(CreateImage(side), start, duration - start, 1)
            : CreateElement(CreateImage(side), MediaTime.Zero, after, 1);
        if (entering)
            element.EnterTransition.Value = CreateTransition(type, loop, frame);
        else
            element.ExitTransition.Value = CreateTransition(type, loop, frame);

        Scene scene = CreateScene(
            frame,
            duration,
            CreateElement(LibraryThumbnailSamples.CreateBackdrop(frame), MediaTime.Zero, duration, 0),
            element);

        int hold = ToFrames(TransitionHold);
        int length = ToFrames(TransitionLength);
        MediaTime shown = entering ? after : MediaTime.Zero;
        MediaTime gone = entering ? MediaTime.Zero : after;
        MediaTime Way(int step) => start + MediaTime.FromFrames(step, LibraryThumbnailLayout.FrameRate);
        MediaTime TimeOf(int index)
        {
            if (index < hold)
                return shown;
            if (index < hold + length)
                return Way(entering ? length - 1 - (index - hold) : index - hold);
            if (index < hold + length + hold)
                return gone;

            int step = index - (hold + length + hold);
            return Way(entering ? step : length - 1 - step);
        }

        return (scene, TimeOf);
    }

    // One of a thumbnail's two transitions; an element holds only its own.
    private static ClipTransition CreateTransition(Type type, MediaTime loop, PixelSize frame)
    {
        var transition = (ClipTransition)Activator.CreateInstance(type)!;
        LibraryThumbnailRecipes.Find(type)?.Apply(transition, new LibraryThumbnailContext(null, loop, frame.ToSize(1)));
        // The frames are laid out for this length, whatever a recipe set.
        transition.Duration.Value = TransitionLength;
        return transition;
    }

    // An image of picture at the size it covers in the project, centred. The picture's image comes through an
    // expression, which does not make it a child of this scene, so every thumbnail drawn from it shares it.
    private static SourceImage CreateImage(LibraryThumbnailSubject.Picture picture)
    {
        return new SourceImage
        {
            Source = { Expression = new SharedValueExpression<ImageSource?>(picture.Source) },
            Transform =
            {
                Value = new ScaleTransform
                {
                    ScaleX = { Value = 100 * picture.Size.Width / picture.Pixels.Width },
                    ScaleY = { Value = 100 * picture.Size.Height / picture.Pixels.Height },
                }
            },
        };
    }

    private static Element CreateElement(Drawable drawable, MediaTime start, MediaTime length, int zIndex)
    {
        var element = new Element
        {
            Start = { Value = start },
            Length = { Value = length },
            ZIndex = { Value = zIndex },
        };
        element.AddObject(drawable);
        return element;
    }

    // The scene and its elements need paths for the scene to relate them, though nothing is read from or written to
    // them.
    private static Scene CreateScene(PixelSize frameSize, MediaTime duration, params ReadOnlySpan<Element> elements)
    {
        string directory = Path.Combine(Path.GetTempPath(), "beutl-library-thumbnail");
        var scene = new Scene(frameSize.Width, frameSize.Height, string.Empty)
        {
            Duration = { Value = duration },
            Uri = ObjectTemplateItem.ToFileUri(Path.Combine(directory, "thumbnail.scene")),
        };
        foreach (Element element in elements)
        {
            element.Uri = ObjectTemplateItem.ToFileUri(Path.Combine(directory, $"{element.Id.Value}.belm"));
            scene.Children.Add(element);
        }

        return scene;
    }

    // One thumbnail being drawn. It lives on the render thread from its creation to its disposal.
    private sealed class Session : IDisposable
    {
        private readonly Type _type;
        private readonly Func<int, MediaTime>? _timeOf;
        private readonly SceneRenderer _renderer;
        private Bitmap? _sheet;

        // The scene is drawn at the scale that makes its frame a thumbnail's width. Resources are shared, so the
        // captured pictures, which stay decoded, are not decoded again; nothing else in the scene is shared.
        private Session(Type type, Scene scene, Func<int, MediaTime>? timeOf, PixelSize sheetSize)
        {
            _type = type;
            _timeOf = timeOf;
            float scale = LibraryThumbnailLayout.FrameSize.Width / (float)scene.FrameSize.Value.Width;
            _renderer = new SceneRenderer(
                scene,
                RenderIntent.Preview,
                scale,
                disableResourceShare: false,
                maxWorkingScale: WorkingScaleCeiling.Preview(scale));
            try
            {
                _renderer.CacheOptions = RenderCacheOptions.Disabled;
                _sheet = new Bitmap(
                    sheetSize.Width, sheetSize.Height, BitmapColorType.Bgra8888, BitmapAlphaType.Premul, BitmapColorSpace.Srgb);
            }
            catch
            {
                _renderer.Dispose();
                throw;
            }
        }

        public static Session? TryCreate(
            Type type, LibraryThumbnailLayout layout, LibraryThumbnailSubject? subject, bool stillOnly)
        {
            try
            {
                (Scene scene, Func<int, MediaTime>? timeOf) = type.IsAssignableTo(typeof(ClipTransition))
                    ? CreateTransitionScene(type, subject)
                    : (CreateEffectScene(type, subject), null);
                return new Session(type, scene, timeOf, stillOnly ? LibraryThumbnailLayout.FrameSize : layout.SheetSize);
            }
            catch (Exception ex)
            {
                // A type from an extension can throw while it is created or set up; the library then shows its name
                // without a picture.
                s_logger.LogDebug(ex, "Could not set up the library thumbnail of {Type}.", type);
                return null;
            }
        }

        // Draws frame `index` of the thumbnail into `cell` of the sheet.
        public bool TryRenderFrame(int index, PixelRect cell)
        {
            if (_sheet == null)
                return false;

            try
            {
                MediaTime time = _timeOf?.Invoke(index) ?? MediaTime.FromFrames(index, LibraryThumbnailLayout.FrameRate);
                CompositionFrame frame = _renderer.Compositor.EvaluateGraphics(time, LibraryThumbnailLayout.FrameRate);
                _renderer.Render(frame);
                using Bitmap snapshot = _renderer.Snapshot();
                using Bitmap converted = snapshot.Convert(
                    BitmapColorType.Bgra8888, BitmapAlphaType.Premul, BitmapColorSpace.Srgb);
                _sheet.CopyFrom(converted, cell);
                return true;
            }
            catch (Exception ex)
            {
                // Such as a shader effect on a machine without the GPU features it needs.
                s_logger.LogDebug(ex, "Could not draw frame {Index} of the library thumbnail of {Type}.", index, _type);
                return false;
            }
        }

        public Bitmap? TakeSheet()
        {
            Bitmap? sheet = _sheet;
            _sheet = null;
            return sheet;
        }

        public void Dispose()
        {
            _renderer.Dispose();
            _sheet?.Dispose();
            _sheet = null;
        }
    }
}
