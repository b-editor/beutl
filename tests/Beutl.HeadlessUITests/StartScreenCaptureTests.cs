using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.NUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Configuration;
using Beutl.Controls.Styling.Themes;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.Views;
using SkiaSharp;

namespace Beutl.HeadlessUITests;

// Produces PNG captures of the start screen for manual design review.
[TestFixture]
[Explicit("Produces PNG captures for manual design review; not a regression test.")]
public class StartScreenCaptureTests
{
    private static readonly (string Name, SKColor Top, SKColor Bottom, SKColor Accent)[] s_projects =
    [
        ("Summer trailer", new SKColor(0xFF, 0x8A, 0x4C), new SKColor(0x4A, 0x1B, 0x5E), new SKColor(0xFF, 0xE0, 0x8A)),
        ("Interview cut", new SKColor(0x26, 0x32, 0x3B), new SKColor(0x0C, 0x10, 0x14), new SKColor(0xD8, 0xB4, 0x9A)),
        ("Product launch", new SKColor(0xE8, 0xEC, 0xF1), new SKColor(0xB6, 0xC2, 0xCF), new SKColor(0x2E, 0x5B, 0xE6)),
        ("Wedding highlights", new SKColor(0xF3, 0xD9, 0xC4), new SKColor(0x9C, 0x7A, 0x6B), new SKColor(0xFF, 0xF6, 0xEA)),
        ("Travel vlog", new SKColor(0x4F, 0xB3, 0xD9), new SKColor(0x1E, 0x6A, 0x5B), new SKColor(0xF5, 0xF1, 0xD0)),
        ("Music video", new SKColor(0x18, 0x08, 0x3A), new SKColor(0xB0, 0x12, 0x6F), new SKColor(0x3C, 0xF0, 0xE0)),
    ];

    private static string[] SeedProjects(ViewConfig viewConfig, bool withThumbnails)
    {
        string workspace = TestShell.NewWorkspace("start-screen-capture-" + Guid.NewGuid().ToString("N"));
        var files = new List<string>();
        viewConfig.RecentProjects.Clear();
        for (int i = 0; i < s_projects.Length; i++)
        {
            (string name, SKColor top, SKColor bottom, SKColor accent) = s_projects[i];
            string folder = Path.Combine(workspace, name);
            Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, name + ".bep");
            File.WriteAllText(file, "{}");
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddHours(-(i * i * 7) - 1));
            // Two of them have never been closed since thumbnails were introduced.
            if (withThumbnails && i != 3)
            {
                using Bitmap frame = DrawFrame(i == 1 ? 1080 : 1920, i == 1 ? 1920 : 1080, top, bottom, accent);
                ProjectThumbnailService.Save(frame, file);
                File.SetLastWriteTimeUtc(ProjectThumbnailService.GetPath(file), DateTime.UtcNow.AddHours(-(i * i * 7) - 1));
            }

            files.Add(file);
        }

        foreach (string file in Enumerable.Reverse(files))
        {
            viewConfig.UpdateRecentProject(file);
        }

        return [.. files];
    }

    private static Bitmap DrawFrame(int width, int height, SKColor top, SKColor bottom, SKColor accent)
    {
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb()));
        using var canvas = new SKCanvas(bitmap);
        using var gradient = SKShader.CreateLinearGradient(
            new SKPoint(0, 0), new SKPoint(0, height), [top, bottom], SKShaderTileMode.Clamp);
        using (var paint = new SKPaint { Shader = gradient })
        {
            canvas.DrawRect(0, 0, width, height, paint);
        }

        using (var paint = new SKPaint { Color = accent, IsAntialias = true })
        {
            canvas.DrawCircle(width * 0.68f, height * 0.42f, Math.Min(width, height) * 0.16f, paint);
        }

        using (var paint = new SKPaint { Color = bottom.WithAlpha(235), IsAntialias = true })
        {
            canvas.DrawRect(0, height * 0.72f, width, height * 0.28f, paint);
        }

        return new Bitmap(bitmap);
    }

    private static async Task WaitForItemsAsync(EditorHostFallback view)
    {
        ListBox list = view.FindControl<ListBox>("recentList")!;
        await HeadlessTestHelpers.WaitUntilAsync(
            () => list.Items.OfType<RecentProjectItem>().All(item => item.Status.Value is not null),
            TimeSpan.FromSeconds(20));
        HeadlessTestHelpers.Render(5);
    }

    [AvaloniaTest]
    [TestCase("dark", 1280, 720)]
    [TestCase("light", 1280, 720)]
    [TestCase("dark", 1920, 1080)]
    [TestCase("dark", 640, 720)]
    public async Task Capture_start_screen(string theme, int width, int height)
    {
        ThemeCaptureTests.UseCaptureTheme(
            theme == "dark" ? ThemeVariant.Dark : ThemeVariant.Light,
            theme == "dark" ? BeutlDarkBorderTheme.AccentColor : null);
        await TestReset.ResetShellAsync();

        ViewConfig viewConfig = GlobalConfiguration.Instance.ViewConfig;
        string[] savedRecentProjects = [.. viewConfig.RecentProjects];
        RecentProjectsLayout savedLayout = viewConfig.RecentProjectsLayout.Value;
        var view = new MainView { DataContext = TestShell.MainViewModel };
        var window = new Window { Width = width, Height = height };
        string suffix = $"{theme}-{width}x{height}";
        try
        {
            viewConfig.RecentProjectsLayout.Value = RecentProjectsLayout.Grid;
            viewConfig.RecentProjects.Clear();
            window.Show();
            window.Content = view;
            HeadlessTestHelpers.Render(5);
            ThemeCaptureTests.Capture(window, $"start-empty-{suffix}.png");

            string[] files = SeedProjects(viewConfig, withThumbnails: true);
            EditorHostFallback fallback = view.GetVisualDescendants().OfType<EditorHostFallback>().Single();
            await WaitForItemsAsync(fallback);
            ListBox list = fallback.FindControl<ListBox>("recentList")!;
            list.SelectedIndex = 0;
            HeadlessTestHelpers.Render(5);
            ThemeCaptureTests.Capture(window, $"start-grid-{suffix}.png");

            fallback.FindControl<TabStrip>("layoutTabs")!.SelectedIndex = 1;
            HeadlessTestHelpers.Render(5);
            ThemeCaptureTests.Capture(window, $"start-list-{suffix}.png");

            fallback.FindControl<TabStrip>("layoutTabs")!.SelectedIndex = 0;
            await TestShell.CreateProjectAsync("open-project-" + suffix);
            TestShell.Editor.SelectedTabItem.Value = null;
            await WaitForItemsAsync(fallback);
            HeadlessTestHelpers.Render(5);
            ThemeCaptureTests.Capture(window, $"start-project-open-{suffix}.png");

            // A frame the app drew itself, kept by closing the project.
            (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync(
                "Rendered frame " + suffix, 1920, 1080, duration: MediaTime.FromSeconds(2), reset: false);
            await TestShell.AddElementAsync(editor, () =>
            {
                var rect = new RectShape();
                rect.Width.Value = 1920;
                rect.Height.Value = 1080;
                rect.Fill.Value = new SolidColorBrush(Color.FromRgb(0x1B, 0x3A, 0x6B));
                return rect;
            }, MediaTime.Zero, MediaTime.FromSeconds(2));
            await TestShell.AddElementAsync(editor, () =>
            {
                var ellipse = new EllipseShape();
                ellipse.Width.Value = 500;
                ellipse.Height.Value = 500;
                ellipse.Fill.Value = new SolidColorBrush(Color.FromRgb(0xF2, 0xA6, 0x3B));
                return ellipse;
            }, MediaTime.Zero, MediaTime.FromSeconds(2), layer: 1);
            HeadlessTestHelpers.Render(5);
            await TestShell.Project.CloseProjectAsync();
            await WaitForItemsAsync(fallback);
            ThemeCaptureTests.Capture(window, $"start-after-close-{suffix}.png");
        }
        finally
        {
            view.DataContext = null;
            window.Close();
            HeadlessTestHelpers.Settle();
            viewConfig.RecentProjectsLayout.Value = savedLayout;
            viewConfig.RecentProjects.Clear();
            viewConfig.RecentProjects.AddRange(savedRecentProjects);
            await TestReset.ResetShellAsync();
        }
    }
}
