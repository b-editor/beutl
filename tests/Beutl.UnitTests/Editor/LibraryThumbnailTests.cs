using Beutl.Editor.Services;
using Beutl.Editor.Services.LibraryThumbnails;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Graphics.Transitions;
using Beutl.Media;
using Beutl.Media.Pixel;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.Services;

namespace Beutl.UnitTests.Editor;

[TestFixture]
public class LibraryThumbnailTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"library-thumbnail-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
        LibraryRegistrar.RegisterAll();
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_directory, recursive: true);
    }

    // Every effect and transition the library lists draws, so no tile of the galleries stays empty.
    [Test]
    public async Task Every_builtin_effect_and_transition_draws_its_still_on_the_samples()
    {
        TestGpu.RequireAnyDevice();
        var failed = new List<string>();
        foreach (Type type in LibraryTypes())
        {
            using LibraryThumbnailSheet? still = await LibraryThumbnailRenderer.RenderAsync(type, null, stillOnly: true);
            if (still == null)
                failed.Add(type.Name);
            else
                Assert.That((still.Bitmap.Width, still.Bitmap.Height), Is.EqualTo((320, 180)), type.Name);
        }

        Assert.That(failed, Is.Empty);
    }

    // A recipe moves the effect from nothing at the start to the most at the frame shown at rest.
    [Test]
    public async Task A_recipe_moves_the_effect_between_the_first_frame_and_the_still()
    {
        TestGpu.RequireAnyDevice();
        using LibraryThumbnailSheet sheet = (await LibraryThumbnailRenderer.RenderAsync(typeof(Invert)))!;
        using Bitmap first = sheet.Bitmap.ExtractSubset(sheet.Layout.GetFrameRect(0));
        using Bitmap still = sheet.Bitmap.ExtractSubset(sheet.Layout.GetFrameRect(sheet.Layout.StillIndex));

        Assert.That(Difference(first, still), Is.GreaterThan(40), "The still inverts what the first frame leaves alone.");
    }

    // The library shows effects on the frame the preview has drawn, taken as it is rather than drawn again.
    [Test]
    public async Task An_effect_draws_on_the_previews_frame()
    {
        TestGpu.RequireAnyDevice();
        using LibraryThumbnailSubject? subject = await LibraryThumbnailSubject.FromFrameAsync(
            Ref<Bitmap>.Create(CreateFrame(960, 540, Colors.Red)), new PixelSize(1920, 1080));
        using LibraryThumbnailSheet still = (await LibraryThumbnailRenderer.RenderAsync(typeof(Invert), subject, stillOnly: true))!;
        Bgra8888 center = Read(still.Bitmap, 160, 90);

        Assert.Multiple(() =>
        {
            Assert.That(subject!.EffectPicture!.Size, Is.EqualTo(new Size(1920, 1080)), "The frame covers the project's frame.");
            Assert.That(subject.EffectArea, Is.EqualTo(new Size(1920, 1080)), "Nothing is left around it.");
            Assert.That(center.R, Is.LessThan(60), "The red frame is inverted to cyan.");
            Assert.That(center.G, Is.GreaterThan(200));
            Assert.That(center.B, Is.GreaterThan(200));
        });
    }

    // A captured picture is decoded once, and every thumbnail drawn from it shares that: its file is gone by the time
    // any is drawn.
    [Test]
    public async Task Thumbnails_draw_a_picture_without_reading_its_file_again()
    {
        TestGpu.RequireAnyDevice();
        using LibraryThumbnailSubject? subject = await LibraryThumbnailSubject.FromFrameAsync(
            Ref<Bitmap>.Create(CreateFrame(64, 36, Colors.Red)), new PixelSize(1920, 1080));
        Assert.That(File.Exists(subject!.EffectPicture!.Source.Uri!.LocalPath), Is.False);

        foreach (Type type in new[] { typeof(Invert), typeof(Negaposi) })
        {
            using LibraryThumbnailSheet still = (await LibraryThumbnailRenderer.RenderAsync(type, subject, stillOnly: true))!;
            Bgra8888 center = Read(still.Bitmap, 160, 90);

            Assert.Multiple(() =>
            {
                Assert.That(center.R, Is.LessThan(60), $"{type.Name} draws the red frame inverted to cyan.");
                Assert.That(center.G, Is.GreaterThan(200), type.Name);
                Assert.That(center.B, Is.GreaterThan(200), type.Name);
            });
        }
    }

    // A transition goes out and comes back in, so its loop has no jump: with one side, the preview's frame leaves for
    // nothing and comes back.
    [Test]
    public async Task A_transition_goes_out_and_back_in()
    {
        TestGpu.RequireAnyDevice();
        using LibraryThumbnailSubject? subject = await LibraryThumbnailSubject.FromFrameAsync(
            Ref<Bitmap>.Create(CreateFrame(64, 36, Colors.Red)), new PixelSize(1920, 1080));
        using LibraryThumbnailSheet sheet = (await LibraryThumbnailRenderer.RenderAsync(typeof(FadeTransition), subject, stillOnly: false))!;
        int hold = (int)Math.Round(LibraryThumbnailRenderer.TransitionHold.ToSeconds() * 24);
        int length = (int)Math.Round(LibraryThumbnailRenderer.TransitionLength.ToSeconds() * 24);

        Bgra8888 Center(int index)
        {
            using Bitmap frame = sheet.Bitmap.ExtractSubset(sheet.Layout.GetFrameRect(index));
            return Read(frame, 160, 90);
        }

        Assert.Multiple(() =>
        {
            Assert.That(Center(0).R, Is.GreaterThan(200), "The frame shows first.");
            Assert.That(Center(hold + length + (hold / 2)).R, Is.LessThan(100), "It has gone out to nothing.");
            Assert.That(Center(sheet.Layout.FrameCount - 1).R, Is.GreaterThan(200), "It is back in as the loop ends.");
        });
    }

    // A side alone goes out the way it comes in, played backwards. A slide moves the incoming side over a still
    // outgoing one, so going out to nothing it would leave the frame whole until it vanished.
    [Test]
    public async Task A_side_alone_goes_out_the_way_it_comes_in()
    {
        TestGpu.RequireAnyDevice();
        using LibraryThumbnailSubject? subject = await LibraryThumbnailSubject.FromFrameAsync(
            Ref<Bitmap>.Create(CreateFrame(64, 36, Colors.Red)), new PixelSize(1920, 1080));
        using LibraryThumbnailSheet sheet = (await LibraryThumbnailRenderer.RenderAsync(typeof(SlideTransition), subject, stillOnly: false))!;
        int hold = (int)Math.Round(LibraryThumbnailRenderer.TransitionHold.ToSeconds() * 24);
        int length = (int)Math.Round(LibraryThumbnailRenderer.TransitionLength.ToSeconds() * 24);

        int Covered(int index)
        {
            using Bitmap frame = sheet.Bitmap.ExtractSubset(sheet.Layout.GetFrameRect(index));
            int covered = 0;
            for (int y = 0; y < frame.Height; y++)
            {
                covered += CountRed(frame, y);
            }

            return covered;
        }

        const int Whole = 320 * 180;
        Assert.Multiple(() =>
        {
            Assert.That(Covered(0), Is.GreaterThan(Whole * 0.95), "The frame shows whole first.");
            Assert.That(Covered(hold + (length / 2)), Is.InRange(Whole / 4, Whole * 3 / 4), "Half way out, half of it has left.");
        });
    }

    // An effect picker shows effects on the object it adds to, cropped to what it draws, at the size it has in the
    // project, with room around it in the thumbnail.
    [Test]
    public async Task An_element_is_captured_alone_at_its_size_in_the_project()
    {
        TestGpu.RequireAnyDevice();
        Scene scene = CreateScene();
        AddElement(scene, CreateRect(1920, 1080, Colors.Blue), 0, 4, 0);
        Element small = AddElement(scene, CreateRect(400, 200, Colors.Red), 0, 4, 1);

        using LibraryThumbnailSubject? subject = await LibraryThumbnailSubject.CaptureElementAsync(small, MediaTime.FromSeconds(1));
        LibraryThumbnailSubject.Picture picture = subject!.EffectPicture!;
        Bitmap captured = picture.Image;
        Bgra8888 center = Read(captured, captured.Width / 2, captured.Height / 2);
        Bgra8888 corner = Read(captured, 1, 1);

        // The room on every side is 12% of the longer side: 48 units around 400 x 200.
        Assert.Multiple(() =>
        {
            Assert.That(picture.Size.Width, Is.EqualTo(400).Within(2));
            Assert.That(picture.Size.Height, Is.EqualTo(200).Within(2));
            Assert.That(picture.Pixels.Width, Is.EqualTo(400).Within(2), "A small object is captured at the project's own density.");
            Assert.That(subject.EffectArea.Width, Is.EqualTo(496).Within(3));
            Assert.That(subject.EffectArea.Height, Is.EqualTo(296).Within(3));
            Assert.That(center.R, Is.GreaterThan(200), "The element itself, without the layer beneath.");
            Assert.That(corner.R, Is.GreaterThan(200), "Nothing is kept around what it draws.");
        });
    }

    // An effect applies to content at the size it has in the project and to its own bounds, and only the result is
    // scaled down: a crop of 100 units from each side leaves half of a 400-unit object, however small the thumbnail
    // shows it.
    [Test]
    public async Task An_effect_applies_at_the_size_content_has_in_the_project()
    {
        TestGpu.RequireAnyDevice();
        Scene scene = CreateScene();
        Element small = AddElement(scene, CreateRect(400, 200, Colors.Red), 0, 4, 1);
        using LibraryThumbnailSubject? subject = await LibraryThumbnailSubject.CaptureElementAsync(small, MediaTime.FromSeconds(1));
        LibraryThumbnailRecipes.Register<Clipping>((effect, _) =>
        {
            effect.Left.Value = 100;
            effect.Right.Value = 100;
        });
        try
        {
            using LibraryThumbnailSheet still = (await LibraryThumbnailRenderer.RenderAsync(typeof(Clipping), subject, stillOnly: true))!;
            PixelSize frame = LibraryThumbnailRenderer.FrameAround(subject!.EffectArea);
            float unit = 320f / frame.Width;

            Assert.That(CountRed(still.Bitmap, 90), Is.EqualTo(200 * unit).Within(3));
        }
        finally
        {
            // Puts the built-in recipes back.
            BuiltInLibraryThumbnailRecipes.Register();
        }
    }

    // Content shows in the smallest 16:9 frame that holds it, in whole steps, so its own size is kept.
    [TestCase(1920, 1080, 1920, 1080)]
    [TestCase(3840, 2160, 3840, 2160)]
    [TestCase(1080, 1920, 3424, 1926)]
    [TestCase(496, 296, 528, 297)]
    public void Content_is_framed_in_16_by_9_around_its_own_size(float width, float height, int frameWidth, int frameHeight)
    {
        Assert.That(LibraryThumbnailRenderer.FrameAround(new Size(width, height)), Is.EqualTo(new PixelSize(frameWidth, frameHeight)));
    }

    // A transition picker shows transitions between the two sides of the edge it sets, or one side and nothing.
    [Test]
    public async Task An_edge_is_captured_as_its_two_sides()
    {
        TestGpu.RequireAnyDevice();
        Scene scene = CreateScene();
        Element first = AddElement(scene, CreateRect(1920, 1080, Colors.Red), 0, 2, 0);
        Element second = AddElement(scene, CreateRect(1920, 1080, Colors.Blue), 2, 2, 0);

        using LibraryThumbnailSubject? between = await LibraryThumbnailSubject.CaptureEdgeAsync(second, ElementEdge.Start);
        using LibraryThumbnailSubject? before = await LibraryThumbnailSubject.CaptureEdgeAsync(first, ElementEdge.Start);
        using LibraryThumbnailSubject? after = await LibraryThumbnailSubject.CaptureEdgeAsync(second, ElementEdge.End);

        Bitmap from = between!.TransitionFrom!.Image;
        Bitmap to = between.TransitionTo!.Image;
        Assert.Multiple(() =>
        {
            Assert.That(Read(from, 10, 10).R, Is.GreaterThan(200), "The outgoing side is the clip before.");
            Assert.That(Read(to, 10, 10).B, Is.GreaterThan(200), "The incoming side is the clip after.");
            Assert.That(between.TransitionFrom.Size, Is.EqualTo(new Size(1920, 1080)));
            Assert.That(before!.TransitionFrom, Is.Null);
            Assert.That(before.TransitionTo, Is.Not.Null);
            Assert.That(after!.TransitionFrom, Is.Not.Null);
            Assert.That(after.TransitionTo, Is.Null);
        });
    }

    // Where nothing is drawn, the thumbnails fall back to the samples.
    [Test]
    public async Task A_frame_with_nothing_drawn_leaves_the_samples()
    {
        using LibraryThumbnailSubject? subject = await LibraryThumbnailSubject.FromFrameAsync(
            Ref<Bitmap>.Create(new Bitmap(64, 36)), new PixelSize(1920, 1080));
        using LibraryThumbnailSubject? none = await LibraryThumbnailSubject.FromFrameAsync(null, new PixelSize(1920, 1080));

        Assert.Multiple(() =>
        {
            Assert.That(subject!.EffectPicture, Is.Null);
            Assert.That(subject.HasTransitionSides, Is.False);
            Assert.That(none, Is.Null);
        });
    }

    // A picture passes through a file in a folder of its own, which only the user can open, and both are gone once it
    // is decoded, so the project's content is not left in the shared temp folder.
    [Test]
    public async Task A_capture_leaves_no_file_behind()
    {
        using LibraryThumbnailSubject? subject = await LibraryThumbnailSubject.FromFrameAsync(
            Ref<Bitmap>.Create(CreateFrame(64, 36, Colors.Red)), new PixelSize(1920, 1080));
        string path = subject!.EffectPicture!.Source.Uri!.LocalPath;
        string folder = Path.GetDirectoryName(path)!;

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(path), Is.False);
            Assert.That(Directory.Exists(folder), Is.False);
            Assert.That(Path.GetDirectoryName(folder), Is.EqualTo(Path.TrimEndingDirectorySeparator(Path.GetTempPath())),
                "The folder is a new one in the temp folder, not a shared one of a fixed name.");
            Assert.That(Path.GetFileName(folder), Is.Not.EqualTo("beutl-library-thumbnail"));
        });
    }

    [Test]
    public void A_recipe_is_registered_and_removed_with_its_assembly()
    {
        var changed = new List<Type>();
        void OnChanged(IReadOnlyList<Type> types) => changed.AddRange(types);
        LibraryThumbnailRecipes.Changed += OnChanged;
        try
        {
            LibraryThumbnailRecipes.Register<LibraryThumbnailTestEffect>((effect, context) => effect.Amount.Value = 1);
            Assert.That(LibraryThumbnailRecipes.Find(typeof(LibraryThumbnailTestEffect)), Is.Not.Null);

            TypeUnloadNotifier.NotifyUnloading([typeof(LibraryThumbnailTestEffect)]);

            Assert.Multiple(() =>
            {
                Assert.That(LibraryThumbnailRecipes.Find(typeof(LibraryThumbnailTestEffect)), Is.Null);
                Assert.That(changed, Is.EqualTo(new[] { typeof(LibraryThumbnailTestEffect), typeof(LibraryThumbnailTestEffect) }));
            });
        }
        finally
        {
            LibraryThumbnailRecipes.Changed -= OnChanged;
            LibraryThumbnailRecipes.Unregister<LibraryThumbnailTestEffect>();
        }
    }

    // A built-in recipe is for an effect the library lists, so none is left behind by a renamed or removed effect.
    [Test]
    public void Every_builtin_recipe_is_for_a_listed_effect()
    {
        IReadOnlySet<Type> effects = LibraryService.Current.GetTypesFromFormat(KnownLibraryItemFormats.FilterEffect);
        Type[] withRecipes = typeof(FilterEffect).Assembly.GetTypes()
            .Where(type => LibraryThumbnailRecipes.Find(type) != null)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(withRecipes, Is.Not.Empty);
            Assert.That(withRecipes.Where(type => !effects.Contains(type)), Is.Empty);
        });
    }

    private static IEnumerable<Type> LibraryTypes()
    {
        return LibraryService.Current.GetTypesFromFormat(KnownLibraryItemFormats.FilterEffect)
            .Concat(LibraryService.Current.GetTypesFromFormat(KnownLibraryItemFormats.ClipTransition))
            .OrderBy(type => type.FullName);
    }

    private Scene CreateScene()
    {
        return new Scene(1920, 1080, string.Empty)
        {
            Duration = { Value = MediaTime.FromSeconds(4) },
            Uri = new Uri(Path.Combine(_directory, $"{Guid.NewGuid():N}.scene")),
        };
    }

    private Element AddElement(Scene scene, Drawable drawable, double start, double length, int zIndex)
    {
        var element = new Element
        {
            Start = { Value = MediaTime.FromSeconds(start) },
            Length = { Value = MediaTime.FromSeconds(length) },
            ZIndex = { Value = zIndex },
            Uri = new Uri(Path.Combine(_directory, $"{Guid.NewGuid():N}.belm")),
        };
        element.AddObject(drawable);
        scene.Children.Add(element);
        return element;
    }

    // A frame as the preview draws one: every pixel of one color.
    private static Bitmap CreateFrame(int width, int height, Color color)
    {
        var bitmap = new Bitmap(width, height, BitmapColorType.Bgra8888, BitmapAlphaType.Premul);
        for (int y = 0; y < height; y++)
        {
            bitmap.GetRow<Bgra8888>(y).Fill(new Bgra8888(color.R, color.G, color.B, color.A));
        }

        return bitmap;
    }

    // The red pixels on row `y`.
    private static int CountRed(Bitmap bitmap, int y)
    {
        using Bitmap converted = bitmap.Convert(BitmapColorType.Bgra8888, BitmapAlphaType.Unpremul, BitmapColorSpace.Srgb);
        int count = 0;
        foreach (Bgra8888 pixel in converted.GetRow<Bgra8888>(y))
        {
            if (pixel.R > 160 && pixel.G < 90 && pixel.B < 90)
                count++;
        }

        return count;
    }

    private static RectShape CreateRect(float width, float height, Color color)
    {
        return new RectShape
        {
            Width = { Value = width },
            Height = { Value = height },
            Fill = { Value = new SolidColorBrush(color) },
        };
    }

    private static Bgra8888 Read(Bitmap bitmap, int x, int y)
    {
        using Bitmap converted = bitmap.Convert(BitmapColorType.Bgra8888, BitmapAlphaType.Unpremul, BitmapColorSpace.Srgb);
        return converted.GetRow<Bgra8888>(y)[x];
    }

    // The mean absolute difference of the color channels, from 0 to 255.
    private static double Difference(Bitmap a, Bitmap b)
    {
        long sum = 0;
        for (int y = 0; y < a.Height; y++)
        {
            ReadOnlySpan<byte> rowA = a.GetRow(y);
            ReadOnlySpan<byte> rowB = b.GetRow(y);
            for (int i = 0; i < rowA.Length; i++)
            {
                if (i % 4 != 3)
                    sum += Math.Abs(rowA[i] - rowB[i]);
            }
        }

        return sum / (a.Width * a.Height * 3.0);
    }
}

// An effect of this assembly only, so unloading its assembly in a test touches nothing else.
public sealed partial class LibraryThumbnailTestEffect : FilterEffect
{
    public LibraryThumbnailTestEffect()
    {
        ScanProperties<LibraryThumbnailTestEffect>();
    }

    public IExpressionProperty<float> Amount { get; } = Property.CreateAnimatable(0f);

    public partial class Resource
    {
        protected override void ApplyTo(FilterEffectContext context)
        {
        }
    }
}
