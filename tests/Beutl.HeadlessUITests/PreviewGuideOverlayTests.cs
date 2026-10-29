using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Beutl.Configuration;
using Beutl.Controls;
using Beutl.Editor.Components.PreviewSettingsTab.ViewModels;
using Beutl.Editor.Components.PreviewSettingsTab.Views;
using Beutl.Editor.Components.TimelineTab.Views;
using Beutl.Editor.Services;
using Beutl.Graphics.Shapes;
using Beutl.Language;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.Pages.SettingsPages;
using Beutl.Services.PrimitiveImpls;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.SettingsPages;
using Beutl.Views;
using Microsoft.Extensions.DependencyInjection;
using BtlBitmap = Beutl.Media.Bitmap;
using BtlMatrix = Beutl.Graphics.Matrix;

namespace Beutl.HeadlessUITests;

// The preview's safe areas and composition guides: drawn over the player, never into the rendered frame.
[NonParallelizable]
[TestFixture]
public class PreviewGuideOverlayTests
{
    private (bool Enabled, PreviewGuideKinds Kinds, string Color, float Opacity, int Columns, int Rows, string Ratios) _saved;

    private static EditorConfig Config => GlobalConfiguration.Instance.EditorConfig;

    [SetUp]
    public void SetUp()
    {
        _saved = (Config.IsPreviewGuideEnabled.Value, Config.PreviewGuideKinds.Value, Config.PreviewGuideColor.Value,
            Config.PreviewGuideOpacity.Value, Config.PreviewGuideGridColumns.Value, Config.PreviewGuideGridRows.Value,
            Config.PreviewGuideAspectRatios.Value);
    }

    [TearDown]
    public void TearDown()
    {
        Config.IsPreviewGuideEnabled.Value = _saved.Enabled;
        Config.PreviewGuideKinds.Value = _saved.Kinds;
        Config.PreviewGuideColor.Value = _saved.Color;
        Config.PreviewGuideOpacity.Value = _saved.Opacity;
        Config.PreviewGuideGridColumns.Value = _saved.Columns;
        Config.PreviewGuideGridRows.Value = _saved.Rows;
        Config.PreviewGuideAspectRatios.Value = _saved.Ratios;
    }

    [Test]
    public void The_safe_areas_are_centered_fractions_of_the_frame()
    {
        var layout = PreviewGuideLayout.Create(
            new Size(960, 540), PreviewGuideKinds.TitleSafe | PreviewGuideKinds.ActionSafe, 1, 1, []);

        Assert.That(layout.Areas, Has.Count.EqualTo(2));
        AssertRect(layout.Areas[0], 24, 13.5, 912, 513);
        AssertRect(layout.Areas[1], 48, 27, 864, 486);
        Assert.That(layout.Lines, Is.Empty);
    }

    [Test]
    public void The_thirds_and_the_custom_grid_divide_the_frame()
    {
        var layout = PreviewGuideLayout.Create(
            new Size(960, 540), PreviewGuideKinds.RuleOfThirds | PreviewGuideKinds.CustomGrid, 4, 2, []);

        Assert.That(VerticalAt(layout), Is.EqualTo(new[] { 320.0, 640.0, 240.0, 480.0, 720.0 }).Within(1e-9));
        Assert.That(HorizontalAt(layout), Is.EqualTo(new[] { 180.0, 360.0, 270.0 }).Within(1e-9));
    }

    [Test]
    public void The_diagonals_and_the_center_cross_meet_at_the_center()
    {
        var layout = PreviewGuideLayout.Create(
            new Size(960, 540), PreviewGuideKinds.Diagonals | PreviewGuideKinds.CenterCross, 1, 1, []);

        Assert.That(layout.Lines, Has.Count.EqualTo(4));
        AssertLine(layout.Lines[0], 0, 0, 960, 540);
        AssertLine(layout.Lines[1], 960, 0, 0, 540);
        // Arms of 5% of the shorter side.
        AssertLine(layout.Lines[2], 453, 270, 507, 270);
        AssertLine(layout.Lines[3], 480, 243, 480, 297);
    }

    // Each ratio is the largest centered frame of its shape; the frame's own ratio would only outline the frame.
    [Test]
    public void Each_aspect_ratio_is_the_largest_centered_frame_of_its_shape()
    {
        PreviewGuideAspectRatio.TryParseList("16:9, 9:16, 2.39:1", out PreviewGuideAspectRatio[] ratios);
        var layout = PreviewGuideLayout.Create(new Size(960, 540), PreviewGuideKinds.AspectRatio, 1, 1, ratios);

        Assert.That(layout.AspectFrames.Select(f => f.Ratio.ToString()), Is.EqualTo(new[] { "9:16", "2.39:1" }));
        AssertRect(layout.AspectFrames[0].Frame, 328.125, 0, 303.75, 540);
        double height = 960 / 2.39;
        AssertRect(layout.AspectFrames[1].Frame, 0, (540 - height) / 2, 960, height);
    }

    [Test]
    public void Unchecked_guides_draw_nothing()
    {
        PreviewGuideAspectRatio.TryParseList("9:16", out PreviewGuideAspectRatio[] ratios);
        var layout = PreviewGuideLayout.Create(new Size(960, 540), PreviewGuideKinds.TitleSafe, 4, 4, ratios);

        Assert.That(layout.Lines, Is.Empty);
        Assert.That(layout.AspectFrames, Is.Empty);
    }

    [AvaloniaTest]
    public async Task The_guides_draw_over_the_preview_and_stay_out_of_the_frame()
    {
        TestGpu.RequireAnyDevice();
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("preview-guides", 640, 360);
        Config.PreviewGuideKinds.Value = PreviewGuideKinds.RuleOfThirds;
        Config.PreviewGuideColor.Value = "#FFFFFFFF";
        Config.PreviewGuideOpacity.Value = 1;
        Config.IsPreviewGuideEnabled.Value = true;

        var view = new PlayerView { DataContext = editor.Player };
        var window = new Window { Content = view, Width = 800, Height = 600 };
        window.Show();
        try
        {
            HeadlessTestHelpers.Render();
            await RenderPreview(editor);
            PreviewGuideOverlay overlay = view.guideOverlay;
            Assert.That(overlay.IsVisible, Is.True);
            // Layout rounding can take the overlay to the nearest whole pixel of the image's size.
            Assert.That(overlay.Bounds.Width, Is.EqualTo(view.image.Bounds.Width).Within(0.5));
            Assert.That(overlay.Bounds.Height, Is.EqualTo(view.image.Bounds.Height).Within(0.5));

            var third = new Point(overlay.Bounds.Width / 3, overlay.Bounds.Height / 2);
            var cell = new Point(overlay.Bounds.Width / 6, overlay.Bounds.Height / 6);
            using (WriteableBitmap frame = window.CaptureRenderedFrame()!)
            {
                HeadlessCapture.SaveIfRequested(window, nameof(PreviewGuideOverlayTests), "rule-of-thirds");
                Assert.That(Brightest(frame, overlay, third), Is.GreaterThan(100), "the line is drawn");
                Assert.That(Brightest(frame, overlay, cell), Is.LessThan(40), "between the lines stays dark");
            }

            // The rendered frame, which the presentation window, frame export and the encoder take, has no guides.
            using (Ref<BtlBitmap> preview = editor.Player.PreviewImage.Value!.Clone())
            {
                double scale = preview.Value.Width / overlay.Bounds.Width;
                Assert.That(PixelBytes(preview.Value, third * scale), Is.EqualTo(PixelBytes(preview.Value, cell * scale)));
            }

            // The zoom is divided out, so the lines stay a pixel wide on screen.
            editor.Player.FrameMatrix.Value = BtlMatrix.CreateScale(2, 2);
            HeadlessTestHelpers.Render();
            Assert.That(overlay.Zoom, Is.EqualTo(2));
            editor.Player.FrameMatrix.Value = BtlMatrix.Identity;

            Config.IsPreviewGuideEnabled.Value = false;
            HeadlessTestHelpers.Render();
            Assert.That(overlay.IsVisible, Is.False);
            using (WriteableBitmap frame = window.CaptureRenderedFrame()!)
            {
                Assert.That(Brightest(frame, overlay, third), Is.LessThan(40), "turning the guides off removes the line");
            }
        }
        finally
        {
            window.Close();
        }
    }

    // Every kind at once, for a look at the overlay when BEUTL_HEADLESS_CAPTURE_DIR is set.
    [AvaloniaTest]
    public async Task Every_guide_draws_at_once()
    {
        TestGpu.RequireAnyDevice();
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("preview-guides-all", 1920, 1080);
        await TestShell.AddElementAsync(editor, () => new RectShape
        {
            Width = { Value = 1920 },
            Height = { Value = 1080 },
            Fill = { Value = new Beutl.Media.SolidColorBrush(Beutl.Media.Color.FromRgb(0xF4, 0xF4, 0xF4)) },
        }, MediaTime.Zero, MediaTime.FromSeconds(2));
        Config.PreviewGuideKinds.Value = Enum.GetValues<PreviewGuideKinds>()
            .Aggregate(PreviewGuideKinds.None, (kinds, kind) => kinds | kind);
        Config.PreviewGuideColor.Value = "#FFFFFFFF";
        Config.PreviewGuideOpacity.Value = 0.8f;
        Config.PreviewGuideGridColumns.Value = 8;
        Config.PreviewGuideGridRows.Value = 4;
        Config.PreviewGuideAspectRatios.Value = "9:16, 1:1, 4:5, 2.39:1";
        Config.IsPreviewGuideEnabled.Value = true;

        var view = new PlayerView { DataContext = editor.Player };
        var window = new Window { Content = view, Width = 1000, Height = 640 };
        window.Show();
        try
        {
            HeadlessTestHelpers.Render();
            await RenderPreview(editor);
            HeadlessCapture.SaveIfRequested(window, nameof(PreviewGuideOverlayTests), "every-guide");

            PreviewGuideOverlay overlay = view.guideOverlay;
            Assert.That(overlay.Kinds, Is.EqualTo(Config.PreviewGuideKinds.Value));
            // The dark halo keeps a white line visible over a near-white frame.
            var actionSafeLeft = new Point(overlay.Bounds.Width * 0.025, overlay.Bounds.Height * 0.4);
            var clear = new Point(overlay.Bounds.Width * 0.3, overlay.Bounds.Height * 0.4);
            using WriteableBitmap frame = window.CaptureRenderedFrame()!;
            int background = Brightest(frame, overlay, clear);
            Assert.That(background, Is.GreaterThan(200));
            Assert.That(Darkest(frame, overlay, actionSafeLeft), Is.LessThan(background - 30));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task Semicolon_toggles_the_guides_unless_a_selected_clip_takes_it_to_close_a_gap()
    {
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("preview-guides-key");
        await TestShell.AddElementAsync(editor, () => new RectShape(), MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        Config.PreviewGuideKinds.Value = PreviewGuideKinds.TitleSafe;
        Config.IsPreviewGuideEnabled.Value = false;

        // EditorHostView attaches the editor's commands this way.
        var editView = new EditView { DataContext = editor };
        TestShell.MainViewModel.ContextCommandManager.Attach(editView, SceneEditorExtension.Instance);
        var window = new Window { Content = editView, Width = 1200, Height = 700 };
        window.Show();
        try
        {
            HeadlessTestHelpers.Render(3);
            PlayerView player = window.GetVisualDescendants().OfType<PlayerView>().Single();
            player.framePanel.Focus();

            PressSemicolon(window);
            Assert.That(Config.IsPreviewGuideEnabled.Value, Is.True);
            Assert.That(player.guideOverlay.IsVisible, Is.True);

            PressSemicolon(window);
            Assert.That(Config.IsPreviewGuideEnabled.Value, Is.False);

            ElementView clip = window.GetVisualDescendants().OfType<ElementView>().Single();
            Point center = clip.border.TranslatePoint(
                new Point(clip.border.Bounds.Width / 2, clip.border.Bounds.Height / 2), window)!.Value;
            window.MouseDown(center, MouseButton.Left);
            window.MouseUp(center, MouseButton.Left);
            clip.Focus();
            HeadlessTestHelpers.Settle();
            Assert.That(editor.GetRequiredService<IEditorSelection>().SelectedObject.Value, Is.Not.Null);

            PressSemicolon(window);
            Assert.That(Config.IsPreviewGuideEnabled.Value, Is.False, "the timeline's Close gap runs first");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task The_preview_settings_tab_edits_the_guides()
    {
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("preview-guides-tab");
        Config.PreviewGuideKinds.Value = PreviewGuideKinds.TitleSafe;
        Config.IsPreviewGuideEnabled.Value = true;

        using var vm = new PreviewSettingsTabViewModel(editor);
        var view = new PreviewSettingsTabView { DataContext = vm };
        var window = new Window { Content = view, Width = 360, Height = 900 };
        window.Show();
        try
        {
            HeadlessTestHelpers.Render();
            HeadlessCapture.SaveIfRequested(window, nameof(PreviewGuideOverlayTests), "preview-settings-tab");

            CheckBox diagonals = view.GetVisualDescendants().OfType<CheckBox>()
                .Single(box => Equals(box.Content, Strings.PreviewGuide_Diagonals));
            diagonals.IsChecked = true;
            Assert.That(Config.PreviewGuideKinds.Value, Is.EqualTo(PreviewGuideKinds.TitleSafe | PreviewGuideKinds.Diagonals));

            ToggleSwitch toggle = view.FindControl<ToggleSwitch>("GuidesToggle")!;
            toggle.IsChecked = false;
            Assert.That(Config.IsPreviewGuideEnabled.Value, Is.False);
            HeadlessTestHelpers.Render();
            Assert.That(view.FindControl<StackPanel>("GuideSettings")!.IsEffectivelyEnabled, Is.False);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public void The_editor_settings_page_edits_the_guides()
    {
        Config.IsPreviewGuideEnabled.Value = false;
        using var vm = new EditorSettingsPageViewModel();
        var page = new EditorSettingsPage { DataContext = vm };
        var window = new Window { Content = page, Width = 1000, Height = 2400 };
        window.Show();
        try
        {
            HeadlessTestHelpers.Render();

            vm.PreviewGuides.IsEnabled.Value = true;
            Assert.That(Config.IsPreviewGuideEnabled.Value, Is.True);
            page.GetVisualDescendants().OfType<OptionsDisplayItem>()
                .Single(item => Equals(item.Header, SettingsStrings.PreviewGuideKinds)).IsExpanded = true;
            HeadlessTestHelpers.Render();
            // The guides are the page's last section.
            page.GetVisualDescendants().OfType<ScrollViewer>().First().ScrollToEnd();
            HeadlessTestHelpers.Render();
            HeadlessCapture.SaveIfRequested(window, nameof(PreviewGuideOverlayTests), "editor-settings-page");

            CheckBox thirds = page.GetVisualDescendants().OfType<CheckBox>()
                .SingleOrDefault(box => Equals(box.Content, Strings.PreviewGuide_RuleOfThirds))
                ?? throw new AssertionException("The guide check boxes are not in the page.");
            thirds.IsChecked = true;
            Assert.That(Config.PreviewGuideKinds.Value.HasFlag(PreviewGuideKinds.RuleOfThirds), Is.True);
        }
        finally
        {
            window.Close();
        }
    }

    private static void PressSemicolon(Window window)
    {
        window.KeyPress(Key.OemSemicolon, RawInputModifiers.None, PhysicalKey.Semicolon, ";");
        HeadlessTestHelpers.Settle();
    }

    private static double[] VerticalAt(PreviewGuideLayout layout)
        => [.. layout.Lines.Where(line => line.Start.X == line.End.X).Select(line => line.Start.X)];

    private static double[] HorizontalAt(PreviewGuideLayout layout)
        => [.. layout.Lines.Where(line => line.Start.Y == line.End.Y).Select(line => line.Start.Y)];

    private static void AssertLine(GuideLine actual, double x1, double y1, double x2, double y2)
    {
        Assert.Multiple(() =>
        {
            Assert.That(actual.Start.X, Is.EqualTo(x1).Within(1e-9));
            Assert.That(actual.Start.Y, Is.EqualTo(y1).Within(1e-9));
            Assert.That(actual.End.X, Is.EqualTo(x2).Within(1e-9));
            Assert.That(actual.End.Y, Is.EqualTo(y2).Within(1e-9));
        });
    }

    private static void AssertRect(Rect actual, double x, double y, double width, double height)
    {
        Assert.Multiple(() =>
        {
            Assert.That(actual.X, Is.EqualTo(x).Within(1e-9));
            Assert.That(actual.Y, Is.EqualTo(y).Within(1e-9));
            Assert.That(actual.Width, Is.EqualTo(width).Within(1e-9));
            Assert.That(actual.Height, Is.EqualTo(height).Within(1e-9));
        });
    }

    // The brightest gray across a few pixels on a row, since an antialiased line spreads over two.
    private static int Brightest(WriteableBitmap frame, Visual overlay, Point point)
        => Samples(frame, overlay, point).Max();

    private static int Darkest(WriteableBitmap frame, Visual overlay, Point point)
        => Samples(frame, overlay, point).Min();

    private static IEnumerable<int> Samples(WriteableBitmap frame, Visual overlay, Point point)
    {
        Point origin = overlay.TranslatePoint(point, (Visual)TopLevel.GetTopLevel(overlay)!)!.Value;
        for (int dx = -2; dx <= 2; dx++)
        {
            (int r, int g, int b) = HeadlessTestHelpers.ReadPixel(
                frame, new Avalonia.PixelPoint((int)Math.Round(origin.X) + dx, (int)Math.Round(origin.Y)));
            yield return (r + g + b) / 3;
        }
    }

    private static byte[] PixelBytes(BtlBitmap bitmap, Point point)
    {
        int size = bitmap.Info.BytesPerPixel;
        return bitmap.GetRow((int)point.Y).Slice((int)point.X * size, size).ToArray();
    }

    private static async Task RenderPreview(EditViewModel editor)
    {
        var rendered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable subscription = editor.Player.AfterRendered
            .Take(1)
            .Subscribe(_ => rendered.TrySetResult());
        editor.Player.QueuePreviewRender();
        await rendered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        HeadlessTestHelpers.Render();
    }
}
