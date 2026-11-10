using Avalonia.Headless.NUnit;

using Beutl.Controls;
using Beutl.Editor.Components.LibraryThumbnails;
using Beutl.Editor.Services.LibraryThumbnails;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Transitions;
using Beutl.Media;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

// The set draws what views ask for, keeps what it shows across a refresh until it is drawn again, and frees frames
// no one plays. The drawing is stood in for here, so no GPU is needed.
[TestFixture]
public class LibraryThumbnailSetTests
{
    private sealed class FakeRenderer
    {
        public List<(Type Type, bool StillOnly)> Calls { get; } = [];

        public Task<LibraryThumbnailSheet?> Render(
            Type type, LibraryThumbnailSubject? subject, bool stillOnly, CancellationToken cancellationToken)
        {
            Calls.Add((type, stillOnly));
            LibraryThumbnailLayout layout = LibraryThumbnailRenderer.GetLayout(type)!;
            PixelSize size = stillOnly ? LibraryThumbnailLayout.FrameSize : layout.SheetSize;
            var bitmap = new Bitmap(size.Width, size.Height, BitmapColorType.Bgra8888, BitmapAlphaType.Premul);
            return Task.FromResult<LibraryThumbnailSheet?>(new LibraryThumbnailSheet(bitmap, layout));
        }
    }

    private static int s_captures;

    private static Task<LibraryThumbnailSubject?> Capture(CancellationToken cancellationToken)
    {
        s_captures++;
        return Task.FromResult<LibraryThumbnailSubject?>(null);
    }

    private static Task Idle(LibraryThumbnailSet set)
    {
        return HeadlessTestHelpers.WaitUntilAsync(() => set.Idle.IsCompleted);
    }

    [AvaloniaTest]
    public async Task Only_what_is_asked_for_is_drawn_and_a_still_alone_is_drawn_on_its_own()
    {
        var renderer = new FakeRenderer();
        using var set = new LibraryThumbnailSet(null, renderer.Render);
        IAnimatedThumbnailSource blur = set.GetSource(typeof(Blur))!;
        IAnimatedThumbnailSource wipe = set.GetSource(typeof(WipeTransition))!;
        await Idle(set);
        Assert.That(renderer.Calls, Is.Empty, "Nothing is drawn before a view asks.");

        using IDisposable request = blur.RequestStill();
        await HeadlessTestHelpers.WaitUntilAsync(() => blur.Still != null);
        await Idle(set);

        Assert.Multiple(() =>
        {
            Assert.That(renderer.Calls, Is.EqualTo(new[] { (typeof(Blur), true) }));
            Assert.That(blur.Frames, Is.Null);
            Assert.That(wipe.Still, Is.Null);
        });
    }

    [AvaloniaTest]
    public async Task Playing_draws_every_frame_and_starts_at_the_still()
    {
        var renderer = new FakeRenderer();
        using var set = new LibraryThumbnailSet(null, renderer.Render);
        IAnimatedThumbnailSource source = set.GetSource(typeof(Blur))!;

        using IDisposable still = source.RequestStill();
        using IDisposable frames = source.RequestFrames();
        await HeadlessTestHelpers.WaitUntilAsync(() => source.Frames != null);

        LibraryThumbnailLayout layout = LibraryThumbnailRenderer.GetLayout(typeof(Blur))!;
        Assert.Multiple(() =>
        {
            Assert.That(renderer.Calls, Does.Contain((typeof(Blur), false)));
            Assert.That(source.Still, Is.Not.Null);
            Assert.That(source.Frames!.Count, Is.EqualTo(layout.FrameCount));
            Assert.That(source.Frames.FirstIndex, Is.EqualTo(layout.StillIndex));
        });
    }

    [AvaloniaTest]
    public async Task A_refresh_captures_again_and_keeps_the_still_until_it_is_drawn_again()
    {
        var renderer = new FakeRenderer();
        s_captures = 0;
        using var set = new LibraryThumbnailSet(Capture, renderer.Render);
        IAnimatedThumbnailSource source = set.GetSource(typeof(Blur))!;
        using IDisposable request = source.RequestStill();
        await HeadlessTestHelpers.WaitUntilAsync(() => source.Still != null);
        await Idle(set);
        Avalonia.Media.IImage first = source.Still!;

        set.Refresh();
        Assert.That(source.Still, Is.SameAs(first), "The still stays until it is drawn again.");
        await HeadlessTestHelpers.WaitUntilAsync(() => !ReferenceEquals(source.Still, first));
        await Idle(set);

        Assert.Multiple(() =>
        {
            Assert.That(s_captures, Is.EqualTo(2));
            Assert.That(renderer.Calls.Count(call => call.Type == typeof(Blur)), Is.EqualTo(2));
        });
    }

    [AvaloniaTest]
    public async Task A_thumbnail_off_screen_is_drawn_again_only_once_it_is_asked_for()
    {
        var renderer = new FakeRenderer();
        using var set = new LibraryThumbnailSet(Capture, renderer.Render);
        IAnimatedThumbnailSource source = set.GetSource(typeof(Blur))!;
        IDisposable request = source.RequestStill();
        await HeadlessTestHelpers.WaitUntilAsync(() => source.Still != null);
        await Idle(set);
        request.Dispose();

        set.Refresh();
        await Idle(set);
        Assert.That(renderer.Calls, Has.Count.EqualTo(1));

        using IDisposable again = source.RequestStill();
        await HeadlessTestHelpers.WaitUntilAsync(() => renderer.Calls.Count == 2);
    }

    [AvaloniaTest]
    public async Task Only_a_few_frames_no_one_plays_stay_drawn()
    {
        var renderer = new FakeRenderer();
        using var set = new LibraryThumbnailSet(null, renderer.Render);
        Type[] types =
        [
            typeof(Blur), typeof(Sharpen), typeof(Bloom), typeof(Bokeh), typeof(HueRotate), typeof(Invert),
        ];
        var sources = new List<IAnimatedThumbnailSource>();
        foreach (Type type in types)
        {
            IAnimatedThumbnailSource source = set.GetSource(type)!;
            using (source.RequestFrames())
            {
                await HeadlessTestHelpers.WaitUntilAsync(() => source.Frames != null);
            }

            sources.Add(source);
        }

        Assert.Multiple(() =>
        {
            Assert.That(sources.Count(source => source.Frames != null), Is.EqualTo(LibraryThumbnailSet.MaxIdleSheets));
            Assert.That(sources[0].Frames, Is.Null, "The least recently played drop their frames first.");
            Assert.That(sources[^1].Frames, Is.Not.Null);
            Assert.That(sources.All(source => source.Still != null), "Stills stay.");
        });
    }

    [AvaloniaTest]
    public void Types_without_a_thumbnail_have_no_source()
    {
        using var set = new LibraryThumbnailSet(null, new FakeRenderer().Render);
        Assert.Multiple(() =>
        {
            Assert.That(set.GetSource(typeof(Beutl.Audio.Effects.DelayEffect)), Is.Null);
            Assert.That(set.GetSource(typeof(FilterEffect)), Is.Null, "An abstract type cannot be drawn.");
            Assert.That(set.GetSource(typeof(Blur)), Is.SameAs(set.GetSource(typeof(Blur))));
        });
    }

    [AvaloniaTest]
    public async Task Disposing_frees_what_was_drawn()
    {
        var renderer = new FakeRenderer();
        var set = new LibraryThumbnailSet(null, renderer.Render);
        IAnimatedThumbnailSource source = set.GetSource(typeof(Blur))!;
        using IDisposable request = source.RequestStill();
        await HeadlessTestHelpers.WaitUntilAsync(() => source.Still != null);

        set.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(source.Still, Is.Null);
            Assert.That(set.GetSource(typeof(Blur)), Is.Null);
        });
    }
}
