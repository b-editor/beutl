using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;

using Beutl.Controls;
using Beutl.Controls.PropertyEditors;
using Beutl.Controls.Styling.Themes;
using Beutl.Editor.Components.LibraryTab.ViewModels;
using Beutl.Editor.Components.LibraryTab.Views.LibraryViews;
using Beutl.Editor.Components.LibraryThumbnails;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Graphics.Transitions;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.ViewModels;

using Microsoft.Extensions.DependencyInjection;

using AvaPoint = Avalonia.Point;
using AvaThickness = Avalonia.Thickness;

namespace Beutl.HeadlessUITests;

[TestFixture]
[Explicit("Produces PNG captures for manual design review; not a regression test.")]
public class LibraryThumbnailCaptureTests
{
    private static string OutputDirectory =>
        Environment.GetEnvironmentVariable("BEUTL_LIBRARY_THUMBNAIL_CAPTURE_DIR")
        ?? Path.Combine(Path.GetTempPath(), "beutl-library-thumbnail-captures");

    [AvaloniaTest]
    public async Task Capture_the_library_galleries_and_pickers_on_the_projects_content()
    {
        TestGpu.RequireAnyDevice();
        await TestReset.ResetShellAsync();
        ThemeCaptureTests.UseCaptureTheme(ThemeVariant.Dark, BeutlDarkBorderTheme.AccentColor);
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("librarythumbnails", 1920, 1080);
        editor.Scene.Duration.Value = MediaTime.FromSeconds(4);
        (Element title, Element second) = await AddContentAsync(editor);
        editor.GetRequiredService<IEditorClock>().Seek(MediaTime.FromSeconds(1));
        HeadlessTestHelpers.Settle();

        using var library = new LibraryTabViewModel(editor);
        await CaptureGalleryAsync(library, KnownLibraryItemFormats.FilterEffect, "library-effects");
        await CaptureGalleryAsync(library, KnownLibraryItemFormats.ClipTransition, "library-transitions");

        var objectServices = new Services(title, editor.GetRequiredService<IEditorClock>());
        await CapturePickerAsync(
            new SelectLibraryItemDialogViewModel(
                KnownLibraryItemFormats.FilterEffect, typeof(FilterEffect), LibraryThumbnailCapture.ForObject(objectServices)),
            "picker-effects");
        await CapturePickerAsync(
            new SelectLibraryItemDialogViewModel(
                KnownLibraryItemFormats.ClipTransition,
                typeof(ClipTransition),
                LibraryThumbnailCapture.ForEdge(second, ElementEdge.Start)),
            "picker-transitions");
    }

    // Two clips one after the other on the bottom layer, with a title and a shape above them.
    private static async Task<(Element Title, Element Second)> AddContentAsync(EditViewModel editor)
    {
        RectShape Clip(uint top, uint bottom)
        {
            var gradient = new LinearGradientBrush
            {
                StartPoint = { Value = RelativePoint.TopLeft },
                EndPoint = { Value = RelativePoint.BottomRight },
            };
            gradient.GradientStops.Add(new GradientStop(Color.FromUInt32(top), 0));
            gradient.GradientStops.Add(new GradientStop(Color.FromUInt32(bottom), 1));
            return new RectShape { Width = { Value = 1920 }, Height = { Value = 1080 }, Fill = { Value = gradient } };
        }

        var title = new Beutl.Graphics.Shapes.TextBlock
        {
            Text = { Value = "Summer trip" },
            Size = { Value = 180 },
            FontWeight = { Value = FontWeight.Bold },
            Fill = { Value = new SolidColorBrush(Colors.White) },
            Transform = { Value = new TranslateTransform(0, -120) },
        };
        var sun = new EllipseShape
        {
            Width = { Value = 260 },
            Height = { Value = 260 },
            Fill = { Value = new SolidColorBrush(Color.FromUInt32(0xFFFACC15)) },
            Transform = { Value = new TranslateTransform(560, 240) },
        };

        IElementAdder adder = editor.GetRequiredService<IElementAdder>();
        await adder.AddAsync(
        [
            new ElementDescription(MediaTime.Zero, MediaTime.FromSeconds(2), 0,
                new ElementSource.EngineObject(() => Clip(0xFF0EA5E9, 0xFF312E81))),
            new ElementDescription(MediaTime.FromSeconds(2), MediaTime.FromSeconds(2), 0,
                new ElementSource.EngineObject(() => Clip(0xFFF97316, 0xFFBE185D))),
            new ElementDescription(MediaTime.Zero, MediaTime.FromSeconds(4), 1, new ElementSource.EngineObject(() => title)),
            new ElementDescription(MediaTime.Zero, MediaTime.FromSeconds(4), 2, new ElementSource.EngineObject(() => sun)),
        ], CancellationToken.None);
        HeadlessTestHelpers.Settle();

        Element titleElement = editor.Scene.Children.Single(element => element.ZIndex.Value == 1);
        Element second = editor.Scene.Children.Single(element => element.ZIndex.Value == 0 && element.Start.Value > MediaTime.Zero);
        return (titleElement, second);
    }

    private static async Task CaptureGalleryAsync(LibraryTabViewModel library, string format, string name)
    {
        var window = new Window
        {
            Width = 380,
            Height = 720,
            Content = new LibraryGalleryView(format) { DataContext = library, Margin = new AvaThickness(8) },
        };
        window.Show();
        await WaitForStillsAsync(window);
        Capture(window, $"{name}.png");

        // The tile under the pointer plays.
        AnimatedThumbnail first = window.GetVisualDescendants().OfType<AnimatedThumbnail>().First();
        AvaPoint center = HeadlessTestHelpers.Center(first, window);
        window.MouseMove(center, RawInputModifiers.None);
        await HeadlessTestHelpers.WaitUntilAsync(
            () => first.Source?.Frames != null, TimeSpan.FromSeconds(60), "The hovered tile's frames did not load.");
        await PlayAsync(first);
        Capture(window, $"{name}-playing.png");
        window.Close();
    }

    private static async Task CapturePickerAsync(SelectLibraryItemDialogViewModel picker, string name)
    {
        try
        {
            await HeadlessTestHelpers.WaitUntilAsync(() => picker.Items.Count > 0, TimeSpan.FromSeconds(10));
            var presenter = new LibraryItemPickerFlyoutPresenter
            {
                Items = picker.Items,
                CanShowTiles = picker.HasThumbnails,
                ShowTiles = true,
                ShowReferencesTab = picker.HasReferencesTab,
                ReferenceItems = picker.ReferenceItems,
            };
            var window = new Window
            {
                Width = 420,
                Height = 560,
                Content = new Border { Padding = new AvaThickness(16), Child = presenter },
            };
            window.Show();
            await WaitForStillsAsync(window);
            Capture(window, $"{name}.png");

            // The selected tile plays, as it does while the arrow keys move through the tiles.
            presenter.SelectedItem = picker.Items[1];
            AnimatedThumbnail selected = window.GetVisualDescendants().OfType<AnimatedThumbnail>()
                .First(thumbnail => thumbnail.IsPlaying);
            await HeadlessTestHelpers.WaitUntilAsync(
                () => selected.Source?.Frames != null, TimeSpan.FromSeconds(60), "The selected tile's frames did not load.");
            await PlayAsync(selected);
            Capture(window, $"{name}-playing.png");

            presenter.ShowTiles = false;
            HeadlessTestHelpers.Render(3);
            Capture(window, $"{name}-list.png");
            window.Close();
        }
        finally
        {
            picker.ReleaseThumbnails();
        }
    }

    // Waits until every thumbnail on screen shows its still.
    private static Task WaitForStillsAsync(Window window)
    {
        return HeadlessTestHelpers.WaitUntilAsync(
            () =>
            {
                HeadlessTestHelpers.Render();
                List<AnimatedThumbnail> shown = window.GetVisualDescendants().OfType<AnimatedThumbnail>()
                    .Where(thumbnail => thumbnail.IsOnScreen && thumbnail.Source != null)
                    .ToList();
                return shown.Count > 0 && shown.All(thumbnail =>
                    thumbnail.Source!.Still != null || thumbnail.Source is LibraryThumbnailSource { Failed: true });
            },
            TimeSpan.FromSeconds(120),
            "The thumbnails on screen did not draw.");
    }

    // Lets the thumbnail move on from its still for about half a second.
    private static async Task PlayAsync(AnimatedThumbnail thumbnail)
    {
        int start = thumbnail.FrameIndex;
        await HeadlessTestHelpers.WaitUntilAsync(
            () =>
            {
                HeadlessTestHelpers.Render();
                return (thumbnail.FrameIndex - start + 48) % 48 >= 12;
            },
            TimeSpan.FromSeconds(10),
            "The thumbnail did not play.");
    }

    private static void Capture(Window window, string name)
    {
        HeadlessTestHelpers.Render(2);
        using WriteableBitmap? frame = window.CaptureRenderedFrame();
        Assert.That(frame, Is.Not.Null);
        Directory.CreateDirectory(OutputDirectory);
        string path = Path.Combine(OutputDirectory, name);
        frame!.Save(path, PngBitmapEncoderOptions.Default);
        TestContext.Out.WriteLine($"Saved capture: {path}");
    }

    private sealed class Services(Element element, IEditorClock clock) : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(Element))
                return element;
            if (serviceType == typeof(IEditorClock))
                return clock;

            return null;
        }
    }
}
