using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Collections;
using Beutl.Configuration;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.Views;

namespace Beutl.HeadlessUITests;

// Guards EditorHostFallback's "recently used" panel. Its width must cap at its 600px MaxWidth on wide
// windows but shrink below that on narrow / high-DPI ones, rather than holding a fixed 600px width that
// clips. The width checks assert arranged layout bounds only (no pixel readback): frame capture of an
// inflated shell view crashes the headless host on software Vulkan.
[TestFixture, NonParallelizable]
public class EditorHostFallbackTests
{
    private static Grid GetRecentItemsPanel(EditorHostFallback view)
    {
        ListBox recentList = view.GetVisualDescendants()
            .OfType<ListBox>()
            .First(x => x.Name == "recentList");

        // The recent-items panel is the Grid that directly hosts the recent-files ListBox.
        return recentList.GetVisualAncestors().OfType<Grid>().First();
    }

    private static double LeftRelativeTo(Visual descendant, Visual ancestor)
    {
        double x = 0;
        for (Visual? current = descendant;
             current is not null && !ReferenceEquals(current, ancestor);
             current = current.GetVisualParent())
        {
            x += current.Bounds.X;
        }

        return x;
    }

    [AvaloniaTest]
    public void Recent_panel_caps_at_600_on_a_wide_window()
    {
        var view = new EditorHostFallback();
        var window = new Window { Content = view, Width = 1200, Height = 800 };

        try
        {
            window.Show();
            HeadlessTestHelpers.Render();

            Grid panel = GetRecentItemsPanel(view);
            Assert.That(
                panel.Bounds.Width,
                Is.EqualTo(600).Within(1.0),
                "On a wide window the recent-items panel should cap at its 600px MaxWidth.");

            // The panel must stay left-anchored (consistent with the header / social rows above and
            // below), not float to the centre of a wide window.
            Assert.That(
                LeftRelativeTo(panel, view),
                Is.LessThan(200),
                "On a wide window the recent-items panel should remain left-anchored, not centred.");
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public void Recent_panel_shrinks_below_600_on_a_narrow_window()
    {
        var view = new EditorHostFallback();
        var window = new Window { Content = view, Width = 320, Height = 600 };

        try
        {
            window.Show();
            HeadlessTestHelpers.Render();

            Grid panel = GetRecentItemsPanel(view);
            Assert.That(panel.Bounds.Width, Is.GreaterThan(0));
            Assert.That(
                panel.Bounds.Width,
                Is.LessThan(600),
                "On a narrow window the recent-items panel must shrink below 600px instead of clipping.");
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    // RecentFiles.Replace used to raise one Replace event whose old and new item counts differ, which the
    // view's DynamicData pipeline cannot apply: replacing two items with none threw
    // IndexOutOfRangeException, and other lengths threw or left the list out of sync.
    [AvaloniaTest]
    public void Recent_list_follows_whole_list_replacements()
    {
        CoreList<string> recentFiles = GlobalConfiguration.Instance.ViewConfig.RecentFiles;
        string[] original = [.. recentFiles];
        string directory = Path.Combine(Path.GetTempPath(), "beutl-recent-files");
        string a = Path.Combine(directory, "a.txt");
        string b = Path.Combine(directory, "b.txt");
        string c = Path.Combine(directory, "c.txt");
        var view = new EditorHostFallback();
        var window = new Window { Content = view, Width = 1200, Height = 800 };

        try
        {
            window.Show();
            ListBox recentList = view.GetVisualDescendants()
                .OfType<ListBox>()
                .First(x => x.Name == "recentList");
            // Show files as well as projects.
            view.GetVisualDescendants()
                .OfType<ComboBox>()
                .First(x => x.Name == "FilterComboBox")
                .SelectedIndex = 0;

            void AssertShows(params string[] expected)
            {
                HeadlessTestHelpers.Render();
                Assert.That(
                    recentList.Items.Cast<FileInfo>().Select(x => x.FullName),
                    Is.EquivalentTo(expected));
            }

            recentFiles.Clear();
            recentFiles.AddRange(new[] { a, b });
            AssertShows(a, b);

            recentFiles.Replace(Array.Empty<string>());
            AssertShows();

            recentFiles.Replace([a, b, c]);
            AssertShows(a, b, c);

            recentFiles.Replace([c]);
            AssertShows(c);
        }
        finally
        {
            recentFiles.Replace(original);
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    [TestCase(1200, false, false)]
    [TestCase(480, true, false)]
    [TestCase(1200, true, true)]
    [TestCase(480, false, true)]
    public async Task Double_click_shows_progress_only_on_the_opening_row_until_completion(
        int width, bool dark, bool fail)
    {
        await TestReset.ResetShellAsync();
        CoreList<string> recentFiles = GlobalConfiguration.Instance.ViewConfig.RecentFiles;
        string[] original = [.. recentFiles];
        string directory = Path.Combine(Path.GetTempPath(), "beutl-recent-opening-" + Guid.NewGuid().ToString("N"));
        string projectFile = Path.Combine(directory, "Opening project.bep");
        string otherFile = Path.Combine(directory, "Another project.bep");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<ProjectService.ProjectOpenPreparation?>(TaskCreationOptions.RunContinuationsAsynchronously);
        int attempts = 0;
        Func<ProjectService.ProjectOpenAttempt, CancellationToken, Task<ProjectService.ProjectOpenPreparation?>>
            preflight = (_, _) =>
            {
                attempts++;
                started.TrySetResult();
                return release.Task;
            };

        var view = new EditorHostFallback();
        var host = new MainView { DataContext = TestShell.MainViewModel, Content = view };
        var window = new Window
        {
            Width = width,
            Height = 720,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light,
        };
        TestShell.Project.OpeningPreflight += preflight;
        try
        {
            recentFiles.Replace([projectFile, otherFile]);
            window.Show();
            window.Content = host;
            HeadlessTestHelpers.Render();
            ListBox list = view.FindControl<ListBox>("recentList")!;

            ListBoxItem Row(string path) => list.GetRealizedContainers().OfType<ListBoxItem>()
                .Single(item => ((FileInfo)item.DataContext!).FullName == path);
            FluentAvalonia.UI.Controls.FAProgressRing Ring(string path) => Row(path).GetVisualDescendants()
                .OfType<FluentAvalonia.UI.Controls.FAProgressRing>().Single();

            void DoubleClick(string path)
            {
                ListBoxItem row = Row(path);
                Point point = row.TranslatePoint(new Point(24, row.Bounds.Height / 2), window)!.Value;
                for (int click = 0; click < 2; click++)
                {
                    window.MouseDown(point, MouseButton.Left);
                    window.MouseUp(point, MouseButton.Left);
                }
            }

            Assert.That(Ring(projectFile).IsVisible, Is.False);
            double originalRowHeight = Row(projectFile).Bounds.Height;
            DoubleClick(projectFile);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            HeadlessTestHelpers.Render();

            var ring = Ring(projectFile);
            var openingRow = Row(projectFile);
            double ringLeft = ring.TranslatePoint(default, openingRow)!.Value.X;
            Assert.Multiple(() =>
            {
                Assert.That(view.OpeningFile, Is.EqualTo(projectFile));
                Assert.That(ring.IsEffectivelyVisible, Is.True);
                Assert.That(ring.IsIndeterminate, Is.True);
                Assert.That(openingRow.Bounds.Height, Is.EqualTo(originalRowHeight));
                Assert.That(Ring(otherFile).IsVisible, Is.False);
                Assert.That(ringLeft, Is.GreaterThan(openingRow.Bounds.Width - 44));
                Assert.That(ringLeft + ring.Bounds.Width, Is.LessThanOrEqualTo(openingRow.Bounds.Width));
            });

            if (Environment.GetEnvironmentVariable("BEUTL_RECENT_OPENING_CAPTURE_DIR") is { Length: > 0 } captureDirectory)
            {
                await Task.Delay(600);
                HeadlessTestHelpers.Render(3);
                Directory.CreateDirectory(captureDirectory);
                using var image = window.CaptureRenderedFrame();
                Assert.That(image, Is.Not.Null);
                image!.Save(Path.Combine(captureDirectory, $"recent-opening-{width}-{dark}-{fail}.png"), PngBitmapEncoderOptions.Default);
            }

            // Another double click must not move the indicator or start an overlapping open.
            DoubleClick(otherFile);
            Assert.That(attempts, Is.EqualTo(1));
            Assert.That(view.OpeningFile, Is.EqualTo(projectFile));

            // Recreating row containers through filtering must preserve the active indicator.
            ComboBox filter = view.FindControl<ComboBox>("FilterComboBox")!;
            filter.SelectedIndex = 2;
            HeadlessTestHelpers.Render();
            filter.SelectedIndex = 1;
            HeadlessTestHelpers.Render();
            Assert.That(Ring(projectFile).IsEffectivelyVisible, Is.True);
            Assert.That(Ring(otherFile).IsVisible, Is.False);

            if (fail)
                release.TrySetException(new IOException("Project opening failed."));
            else
                release.TrySetResult(null);
            await WaitForOpeningToEndAsync(view);
            HeadlessTestHelpers.Render();
            Assert.That(list.GetVisualDescendants().OfType<FluentAvalonia.UI.Controls.FAProgressRing>()
                .All(control => !control.IsVisible && !control.IsIndeterminate), Is.True);
        }
        finally
        {
            release.TrySetResult(null);
            try
            {
                await WaitForOpeningToEndAsync(view);
            }
            finally
            {
                TestShell.Project.OpeningPreflight -= preflight;
                window.Close();
                host.DataContext = null;
                recentFiles.Replace(original);
                HeadlessTestHelpers.Settle();
                await TestReset.ResetShellAsync();
            }
        }
    }

    private static async Task WaitForOpeningToEndAsync(EditorHostFallback view)
    {
        var timeout = Stopwatch.StartNew();
        while (view.OpeningFile != null && timeout.Elapsed < TimeSpan.FromSeconds(5))
            await Task.Delay(10);
        Assert.That(view.OpeningFile, Is.Null, "The row must stop showing progress when opening ends.");
    }
}
