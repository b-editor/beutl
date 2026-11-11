using System.Diagnostics;
using System.Reactive;
using System.Reactive.Linq;

using Avalonia.Headless.NUnit;

using Beutl.Editor.Components.LibraryTab.ViewModels;
using Beutl.Editor.Components.LibraryThumbnails;
using Beutl.Editor.Services;
using Beutl.Graphics.Effects;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;

using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

// The library's thumbnails show the frame the preview has drawn, and take it again only once the preview draws
// another.
[TestFixture]
public class LibraryTabPreviewThumbnailTests
{
    private sealed class FakePlayer : IPreviewPlayer
    {
        public ReactivePropertySlim<Ref<Bitmap>?> Frame { get; } = new();

        public IReadOnlyReactiveProperty<Ref<Bitmap>?> PreviewImage => Frame;

        public IObservable<Unit> AfterRendered => Observable.Empty<Unit>();

        public IReadOnlyReactiveProperty<bool> IsPlaying { get; } = new ReactivePropertySlim<bool>(false);
    }

    private sealed class Editor(IPreviewPlayer player, Scene scene) : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            return serviceType == typeof(IPreviewPlayer) ? player : serviceType == typeof(Scene) ? scene : null;
        }
    }

    [AvaloniaTest]
    public async Task The_frame_the_thumbnails_were_drawn_on_is_not_taken_again()
    {
        var player = new FakePlayer();
        using var library = new LibraryTabViewModel(new Editor(player, new Scene(1920, 1080, string.Empty)));
        var source = (LibraryThumbnailSource)library.Thumbnails.GetSource(typeof(Invert))!;
        using IDisposable request = source.RequestStill();
        using Ref<Bitmap> first = CreateFrame();
        using Ref<Bitmap> second = CreateFrame();

        // A gallery coming on screen makes the preview draw a frame, which the thumbnails are drawn on.
        player.Frame.Value = first;
        await HeadlessTestHelpers.WaitUntilAsync(() => source.Still != null || source.Failed, TimeSpan.FromSeconds(30));
        var settled = Stopwatch.StartNew();
        await HeadlessTestHelpers.WaitUntilAsync(() => settled.Elapsed > TimeSpan.FromSeconds(1));
        Assert.That(source.Generation, Is.Zero, "The frame the thumbnails were drawn on is not taken again.");

        // An edit makes the preview draw another frame, which they are drawn on again.
        player.Frame.Value = second;
        await HeadlessTestHelpers.WaitUntilAsync(
            () => source.Generation == 1, TimeSpan.FromSeconds(5), "A new frame was not taken.");
    }

    private static Ref<Bitmap> CreateFrame()
    {
        var bitmap = new Bitmap(64, 36, BitmapColorType.Bgra8888, BitmapAlphaType.Premul);
        for (int y = 0; y < bitmap.Height; y++)
        {
            bitmap.GetRow<Beutl.Media.Pixel.Bgra8888>(y).Fill(new Beutl.Media.Pixel.Bgra8888(255, 0, 0, 255));
        }

        return Ref<Bitmap>.Create(bitmap);
    }
}
