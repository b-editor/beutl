using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Beutl.Collections;
using Beutl.Configuration;
using Beutl.Editor.Components.FileBrowserTab.Services;
using Beutl.Language;
using Beutl.Media;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.Views;
using FluentAvalonia.UI.Controls;

namespace Beutl.HeadlessUITests;

// Guards EditorHostFallback: its layout, its sync with RecentProjects, the pictures and the progress on the entry being
// opened. The width checks read layout bounds, not pixels: capturing the frame of an inflated shell view crashes the
// headless host on software Vulkan.
[TestFixture, NonParallelizable]
public class EditorHostFallbackTests
{
    private const double RailWidth = 256;
    private const double GridSideMargin = 16;

    private static ListBox RecentList(EditorHostFallback view) => view.FindControl<ListBox>("recentList")!;

    private static TabStrip LayoutTabs(EditorHostFallback view) => view.FindControl<TabStrip>("layoutTabs")!;

    private static string[] Shown(EditorHostFallback view)
        => [.. RecentList(view).Items.Cast<RecentProjectItem>().Select(x => x.FullName)];

    private static ListBoxItem Row(ListBox list, string path) => list.GetRealizedContainers().OfType<ListBoxItem>()
        .Single(item => ((RecentProjectItem)item.DataContext!).FullName == path);

    // Replaces the recent projects and the layout for the test, and puts them back when disposed.
    private sealed class RecentProjectsScope : IDisposable
    {
        private readonly ViewConfig _viewConfig = GlobalConfiguration.Instance.ViewConfig;
        private readonly string[] _files;
        private readonly RecentProjectsLayout _layout;

        public RecentProjectsScope(RecentProjectsLayout layout, params string[] files)
        {
            _files = [.. _viewConfig.RecentProjects];
            _layout = _viewConfig.RecentProjectsLayout.Value;
            _viewConfig.RecentProjectsLayout.Value = layout;
            _viewConfig.RecentProjects.Replace(files);
        }

        public void Dispose()
        {
            _viewConfig.RecentProjectsLayout.Value = _layout;
            _viewConfig.RecentProjects.Replace(_files);
        }
    }

    private static string[] CreateProjectFiles(string name, int count)
    {
        string directory = TestShell.NewWorkspace(name + "-" + Guid.NewGuid().ToString("N"));
        return Enumerable.Range(0, count)
            .Select(i =>
            {
                string folder = Path.Combine(directory, $"project{i}");
                Directory.CreateDirectory(folder);
                string file = Path.Combine(folder, $"project{i}.bep");
                File.WriteAllText(file, "{}");
                return file;
            })
            .ToArray();
    }

    private static async Task WaitForItemsAsync(EditorHostFallback view)
    {
        await HeadlessTestHelpers.WaitUntilAsync(
            () => RecentList(view).Items.Cast<RecentProjectItem>().All(item => item.Status.Value is not null));
        HeadlessTestHelpers.Render();
    }

    [AvaloniaTest]
    public void The_sheet_takes_the_room_beside_the_rail_on_a_wide_window()
    {
        using var recent = new RecentProjectsScope(RecentProjectsLayout.Grid);
        var view = new EditorHostFallback();
        var window = new Window { Content = view, Width = 1200, Height = 800 };

        try
        {
            window.Show();
            HeadlessTestHelpers.Render();

            Control rail = view.FindControl<Control>("rail")!;
            ListBox list = RecentList(view);
            Assert.Multiple(() =>
            {
                Assert.That(rail.Bounds.Width, Is.EqualTo(RailWidth));
                Assert.That(list.TranslatePoint(default, view)!.Value.X, Is.EqualTo(RailWidth));
                Assert.That(list.Bounds.Width, Is.EqualTo(1200 - RailWidth));
            });
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    [TestCase(320)]
    [TestCase(480)]
    public void The_rail_moves_above_the_sheet_on_a_narrow_window(int width)
    {
        using var recent = new RecentProjectsScope(RecentProjectsLayout.Grid);
        var view = new EditorHostFallback();
        var window = new Window { Content = view, Width = width, Height = 600 };

        try
        {
            window.Show();
            HeadlessTestHelpers.Render();

            Control rail = view.FindControl<Control>("rail")!;
            ListBox list = RecentList(view);
            Button createNew = view.FindControl<Button>("createNewButton")!;
            Assert.Multiple(() =>
            {
                Assert.That(rail.Bounds.Width, Is.EqualTo(width));
                Assert.That(list.Bounds.Width, Is.EqualTo(width));
                Assert.That(list.TranslatePoint(default, view)!.Value.Y, Is.GreaterThanOrEqualTo(rail.Bounds.Bottom));
                Assert.That(list.Bounds.Height, Is.GreaterThan(0), "The sheet must keep room to scroll.");
                Assert.That(createNew.TranslatePoint(new Point(createNew.Bounds.Width, 0), view)!.Value.X, Is.LessThanOrEqualTo(width));
                Assert.That(LayoutTabs(view).TranslatePoint(new Point(LayoutTabs(view).Bounds.Width, 0), view)!.Value.X, Is.LessThanOrEqualTo(width));
            });
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    // A cell is at least 232 wide, and the cells of a row fill the list between its margins.
    [AvaloniaTest]
    [TestCase(1200, 3)]
    [TestCase(1600, 5)]
    [TestCase(480, 1)]
    public void The_grid_fills_each_row_with_frames_of_one_shape(int width, int columns)
    {
        string[] files = CreateProjectFiles("grid", 6);
        using var recent = new RecentProjectsScope(RecentProjectsLayout.Grid, files);
        var view = new EditorHostFallback();
        var window = new Window { Content = view, Width = width, Height = 900 };

        try
        {
            window.Show();
            HeadlessTestHelpers.Render(2);

            ListBox list = RecentList(view);
            ListBoxItem[] rows = [.. files.Select(file => Row(list, file))];
            double cellWidth = Math.Floor((list.Bounds.Width - (GridSideMargin * 2)) / columns);
            WideFramePanel frame = rows[0].GetVisualDescendants().OfType<WideFramePanel>().Single();
            int firstRow = rows.Count(row => row.Bounds.Y == rows[0].Bounds.Y);
            Assert.Multiple(() =>
            {
                Assert.That(firstRow, Is.EqualTo(columns));
                Assert.That(rows.Select(row => row.Bounds.Width), Is.All.EqualTo(cellWidth));
                Assert.That(frame.Bounds.Height, Is.EqualTo(frame.Bounds.Width * 9 / 16).Within(1));
            });
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    // Regression: replacing the whole list with a different item count must keep the view in sync.
    [AvaloniaTest]
    public void Recent_list_follows_whole_list_replacements()
    {
        IListProperty<string> recentProjects = GlobalConfiguration.Instance.ViewConfig.RecentProjects;
        string[] original = [.. recentProjects];
        string directory = Path.Combine(Path.GetTempPath(), "beutl-recent-projects");
        string a = Path.Combine(directory, "a.bep");
        string b = Path.Combine(directory, "b.bep");
        string c = Path.Combine(directory, "c.bep");
        var view = new EditorHostFallback();
        var window = new Window { Content = view, Width = 1200, Height = 800 };

        try
        {
            window.Show();

            void AssertShows(params string[] expected)
            {
                HeadlessTestHelpers.Render();
                Assert.That(Shown(view), Is.EqualTo(expected));
            }

            recentProjects.Clear();
            recentProjects.AddRange(new[] { a, b });
            AssertShows(a, b);

            recentProjects.Replace(Array.Empty<string>());
            AssertShows();

            recentProjects.Replace([a, b, c]);
            AssertShows(a, b, c);

            recentProjects.Replace([c]);
            AssertShows(c);
        }
        finally
        {
            recentProjects.Replace(original);
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    // The file system's access times would not do: most Windows volumes do not update them.
    [AvaloniaTest]
    public void The_project_opened_last_comes_first()
    {
        ViewConfig viewConfig = GlobalConfiguration.Instance.ViewConfig;
        string[] files = CreateProjectFiles("order", 3);
        using var recent = new RecentProjectsScope(RecentProjectsLayout.Grid, files);
        var view = new EditorHostFallback();
        var window = new Window { Content = view, Width = 1200, Height = 800 };

        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            Assert.That(Shown(view), Is.EqualTo(files));

            viewConfig.UpdateRecentProject(files[2]);
            HeadlessTestHelpers.Render();

            Assert.That(Shown(view), Is.EqualTo(new[] { files[2], files[0], files[1] }));
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public void Scenes_opened_on_their_own_are_not_listed()
    {
        ViewConfig viewConfig = GlobalConfiguration.Instance.ViewConfig;
        string[] savedFiles = [.. viewConfig.RecentFiles];
        string project = CreateProjectFiles("only-projects", 1)[0];
        using var recent = new RecentProjectsScope(RecentProjectsLayout.Grid, project);
        var view = new EditorHostFallback();
        var window = new Window { Content = view, Width = 1200, Height = 800 };

        try
        {
            viewConfig.RecentFiles.Replace([Path.ChangeExtension(project, ".scene"), project]);
            window.Show();
            HeadlessTestHelpers.Render();

            Assert.That(Shown(view), Is.EqualTo(new[] { project }));
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
            viewConfig.RecentFiles.Replace(savedFiles);
        }
    }

    [AvaloniaTest]
    [TestCase(RecentProjectsLayout.Grid)]
    [TestCase(RecentProjectsLayout.List)]
    public async Task A_saved_picture_replaces_the_placeholder(RecentProjectsLayout layout)
    {
        string[] files = CreateProjectFiles("picture", 2);
        using (var picture = new Bitmap(320, 180))
        {
            ProjectThumbnailService.Save(picture, files[0]);
        }

        using var recent = new RecentProjectsScope(layout, files);
        var view = new EditorHostFallback();
        var window = new Window { Content = view, Width = 1200, Height = 800 };

        try
        {
            window.Show();
            await WaitForItemsAsync(view);

            ListBox list = RecentList(view);
            Image Picture(string file) => Row(list, file).GetVisualDescendants().OfType<Image>().Single();
            RecentProjectItem Item(string file) => (RecentProjectItem)Row(list, file).DataContext!;
            Assert.Multiple(() =>
            {
                Assert.That(Picture(files[0]).Source, Is.Not.Null);
                Assert.That(Item(files[0]).HasNoThumbnail.Value, Is.False);
                Assert.That(Picture(files[1]).Source, Is.Null);
                Assert.That(Item(files[1]).HasNoThumbnail.Value, Is.True);
            });
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task The_layout_choice_is_kept_and_keeps_the_entries()
    {
        ViewConfig viewConfig = GlobalConfiguration.Instance.ViewConfig;
        string[] files = CreateProjectFiles("layout", 2);
        using var recent = new RecentProjectsScope(RecentProjectsLayout.Grid, files);
        var view = new EditorHostFallback();
        var window = new Window { Content = view, Width = 1200, Height = 800 };

        try
        {
            window.Show();
            await WaitForItemsAsync(view);
            ListBox list = RecentList(view);
            Assert.Multiple(() =>
            {
                Assert.That(list.Classes.Contains("grid"), Is.True);
                Assert.That(Row(list, files[0]).GetVisualDescendants().OfType<WideFramePanel>(), Is.Not.Empty);
            });

            LayoutTabs(view).SelectedIndex = 1;
            HeadlessTestHelpers.Render(2);
            Assert.Multiple(() =>
            {
                Assert.That(viewConfig.RecentProjectsLayout.Value, Is.EqualTo(RecentProjectsLayout.List));
                Assert.That(list.Classes.Contains("grid"), Is.False);
                Assert.That(Shown(view), Is.EqualTo(files));
                Assert.That(Row(list, files[0]).GetVisualDescendants().OfType<WideFramePanel>(), Is.Empty);
                Assert.That(Row(list, files[0]).Bounds.Width, Is.EqualTo(list.Bounds.Width).Within(8));
            });

            var next = new EditorHostFallback();
            Assert.Multiple(() =>
            {
                Assert.That(LayoutTabs(next).SelectedIndex, Is.EqualTo(1));
                Assert.That(RecentList(next).Classes.Contains("grid"), Is.False);
            });
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    // A project's close renews its picture behind the editor, so the next showing has to read it.
    [AvaloniaTest]
    public async Task The_pictures_are_let_go_while_hidden_and_read_again_when_shown()
    {
        string file = CreateProjectFiles("hidden", 1)[0];
        using (var picture = new Bitmap(320, 180))
        {
            ProjectThumbnailService.Save(picture, file);
        }

        using var recent = new RecentProjectsScope(RecentProjectsLayout.Grid, file);
        var view = new EditorHostFallback();
        var window = new Window { Content = view, Width = 1200, Height = 800 };

        try
        {
            window.Show();
            await WaitForItemsAsync(view);
            RecentProjectItem shown = RecentList(view).Items.Cast<RecentProjectItem>().Single();
            Assert.That(shown.Thumbnail.Value, Is.Not.Null);

            view.IsVisible = false;
            HeadlessTestHelpers.Render();
            Assert.Multiple(() =>
            {
                Assert.That(RecentList(view).Items, Is.Empty);
                Assert.That(shown.Thumbnail.Value, Is.Null);
            });

            view.IsVisible = true;
            HeadlessTestHelpers.Render();
            await WaitForItemsAsync(view);
            Assert.That(RecentList(view).Items.Cast<RecentProjectItem>().Single().Thumbnail.Value, Is.Not.Null);
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task An_entry_says_when_its_file_is_gone()
    {
        string file = CreateProjectFiles("gone", 1)[0];
        File.Delete(file);
        using var recent = new RecentProjectsScope(RecentProjectsLayout.Grid, file);
        var view = new EditorHostFallback();
        var window = new Window { Content = view, Width = 1200, Height = 800 };

        try
        {
            window.Show();
            await WaitForItemsAsync(view);

            RecentProjectItem item = RecentList(view).Items.Cast<RecentProjectItem>().Single();
            Assert.Multiple(() =>
            {
                Assert.That(item.IsMissing.Value, Is.True);
                Assert.That(item.Status.Value, Is.EqualTo(MessageStrings.FileDoesNotExist));
            });
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public void A_project_goes_by_its_name()
    {
        string folder = Path.Combine(Path.GetTempPath(), "Trailer");
        using var project = new RecentProjectItem(Path.Combine(folder, "Trailer.bep"));
        Assert.Multiple(() =>
        {
            Assert.That(project.Name, Is.EqualTo("Trailer"));
            Assert.That(project.DirectoryName, Is.EqualTo(folder));
            Assert.That(project.HasNoThumbnail.Value, Is.False, "It is not known until the entry is read.");
        });
    }

    [AvaloniaTest]
    public async Task A_damaged_picture_counts_as_none()
    {
        string file = CreateProjectFiles("damaged", 1)[0];
        string picture = ProjectThumbnailService.GetPath(file);
        Directory.CreateDirectory(Path.GetDirectoryName(picture)!);
        File.WriteAllText(picture, "not a picture");

        using var item = new RecentProjectItem(file);
        await item.LoadAsync();

        Assert.Multiple(() =>
        {
            Assert.That(item.Thumbnail.Value, Is.Null);
            Assert.That(item.IsMissing.Value, Is.False);
            Assert.That(item.Status.Value, Is.Not.Null);
        });
    }

    [AvaloniaTest]
    public async Task A_recent_entry_menu_opens_with_its_items()
    {
        string file = CreateProjectFiles("menu", 1)[0];
        using var recent = new RecentProjectsScope(RecentProjectsLayout.Grid, file);
        var view = new EditorHostFallback();
        var window = new Window { Content = view, Width = 640, Height = 480 };

        try
        {
            window.Show();
            await WaitForItemsAsync(view);

            ListBoxItem row = Row(RecentList(view), file);
            var menu = (FAMenuFlyout)row.ContextFlyout!;
            menu.ShowAt(row);
            HeadlessTestHelpers.Render();
            try
            {
                Assert.That(menu.IsOpen, Is.True);
                FAMenuFlyoutItem[] items = [.. menu.Items.OfType<FAMenuFlyoutItem>().Where(item => item.IsVisible)];
                Assert.That(items.Select(item => item.Text), Is.EqualTo(new[]
                {
                    Strings.Open, EditorHostFallback.FileManagerMenuHeader, Strings.Remove, Strings.DeleteFromDisk,
                }));
                Assert.That(items.Select(item => item.Bounds.Height), Is.All.GreaterThan(0));
                Assert.That(items.Select(item => item.DataContext), Is.All.SameAs(row.DataContext), "The handlers read the entry from it.");
            }
            finally
            {
                menu.Hide();
                HeadlessTestHelpers.Settle();
            }
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task The_menu_shows_the_project_file_in_the_file_manager()
    {
        string[] files = CreateProjectFiles("file-manager", 2);
        File.Delete(files[1]);
        using var recent = new RecentProjectsScope(RecentProjectsLayout.Grid, files);
        var launched = new List<ProcessStartInfo>();
        var view = new EditorHostFallback
        {
            LaunchFileManagerAsync = startInfo =>
            {
                launched.Add(startInfo);
                return Task.FromResult(true);
            },
        };
        var window = new Window { Content = view, Width = 1200, Height = 800 };
        using var notifications = new NotificationCapture();

        try
        {
            window.Show();
            await WaitForItemsAsync(view);

            async Task ShowInFileManager(string file)
            {
                ListBoxItem row = Row(RecentList(view), file);
                var menu = (FAMenuFlyout)row.ContextFlyout!;
                menu.ShowAt(row);
                HeadlessTestHelpers.Render();
                FAMenuFlyoutItem item = menu.Items.OfType<FAMenuFlyoutItem>()
                    .Single(x => x.Text == EditorHostFallback.FileManagerMenuHeader);
                item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                menu.Hide();
                await HeadlessTestHelpers.WaitUntilAsync(() => launched.Count > 0 || notifications.All.Any());
            }

            await ShowInFileManager(files[0]);
            ProcessStartInfo expected = FileManagerLauncher.CreateStartInfo(files[0], isDirectory: false);
            Assert.That(launched.Select(x => (x.FileName, x.Arguments, Args: string.Join("|", x.ArgumentList))),
                Is.EqualTo(new[] { (expected.FileName, expected.Arguments, string.Join("|", expected.ArgumentList)) }));

            launched.Clear();
            await ShowInFileManager(files[1]);
            Assert.Multiple(() =>
            {
                Assert.That(launched, Is.Empty, "A project that is gone has nothing to show.");
                Assert.That(notifications.All.Select(x => x.Message), Does.Contain(MessageStrings.FileDoesNotExist));
            });
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    [TestCase(1200, false, RecentProjectsLayout.List)]
    [TestCase(480, false, RecentProjectsLayout.List)]
    [TestCase(1200, true, RecentProjectsLayout.List)]
    [TestCase(480, true, RecentProjectsLayout.List)]
    [TestCase(1200, false, RecentProjectsLayout.Grid)]
    [TestCase(480, true, RecentProjectsLayout.Grid)]
    public async Task A_click_shows_progress_only_on_the_opening_entry_until_completion(
        int width, bool fail, RecentProjectsLayout layout)
    {
        await TestReset.ResetShellAsync();
        string directory = Path.Combine(Path.GetTempPath(), "beutl-recent-opening-" + Guid.NewGuid().ToString("N"));
        string projectFile = Path.Combine(directory, "Opening project.bep");
        string otherFile = Path.Combine(directory, "Another project.bep");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int attempts = 0;
        // Holds the open inside its transition. Neither file is a valid project, so a released open still
        // fails after the handlers, and the failing case fails in them.
        Func<string, Task> opening = _ =>
        {
            attempts++;
            started.TrySetResult();
            return release.Task;
        };
        Directory.CreateDirectory(directory);
        File.WriteAllText(projectFile, "{}");
        File.WriteAllText(otherFile, "{}");

        using var recent = new RecentProjectsScope(layout, projectFile, otherFile);
        var view = new EditorHostFallback();
        var host = new MainView { DataContext = TestShell.MainViewModel, Content = view };
        var window = new Window { Width = width, Height = 720 };
        TestShell.Project.Opening += opening;
        try
        {
            window.Show();
            window.Content = host;
            HeadlessTestHelpers.Render();
            ListBox list = RecentList(view);

            FAProgressRing Ring(string path) => Row(list, path).GetVisualDescendants()
                .OfType<FAProgressRing>().Single();

            void Click(string path)
            {
                ListBoxItem row = Row(list, path);
                Point point = row.TranslatePoint(new Point(24, row.Bounds.Height / 2), window)!.Value;
                TimelineTestHost.WaitForHitTest(window, row, point);
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
            }

            Assert.That(Ring(projectFile).IsVisible, Is.False);
            double originalRowHeight = Row(list, projectFile).Bounds.Height;
            Click(projectFile);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            HeadlessTestHelpers.Render();

            var ring = Ring(projectFile);
            var openingRow = Row(list, projectFile);
            double ringLeft = ring.TranslatePoint(default, openingRow)!.Value.X;
            Assert.Multiple(() =>
            {
                Assert.That(view.OpeningFile, Is.EqualTo(projectFile));
                Assert.That(ring.IsEffectivelyVisible, Is.True);
                Assert.That(ring.IsIndeterminate, Is.True);
                Assert.That(openingRow.Bounds.Height, Is.EqualTo(originalRowHeight));
                Assert.That(Ring(otherFile).IsVisible, Is.False);
                if (layout == RecentProjectsLayout.List)
                {
                    Assert.That(ringLeft, Is.GreaterThanOrEqualTo(openingRow.Bounds.Width - 44));
                    Assert.That(ringLeft + ring.Bounds.Width, Is.LessThanOrEqualTo(openingRow.Bounds.Width));
                }
                else
                {
                    // In the middle of the frame, on a veil that keeps the picture from competing with it.
                    Assert.That(ringLeft + (ring.Bounds.Width / 2), Is.EqualTo(openingRow.Bounds.Width / 2).Within(1));
                    WideFramePanel frame = openingRow.GetVisualDescendants().OfType<WideFramePanel>().Single();
                    Assert.That(frame.GetVisualDescendants().OfType<Border>()
                        .Any(border => border.IsEffectivelyVisible
                            && border.Background is not null
                            && border.Bounds.Width >= frame.Bounds.Width - 1
                            && ReferenceEquals(border.GetVisualParent(), ring.GetVisualParent())), Is.True);
                }
            });

            // Another click must not move the indicator or start an overlapping open.
            Click(otherFile);
            Assert.That(attempts, Is.EqualTo(1));
            Assert.That(view.OpeningFile, Is.EqualTo(projectFile));

            // Recreating the containers through a layout change must preserve the active indicator.
            TabStrip layoutTabs = LayoutTabs(view);
            layoutTabs.SelectedIndex = 1 - layoutTabs.SelectedIndex;
            HeadlessTestHelpers.Render(2);
            Assert.That(Ring(projectFile).IsEffectivelyVisible, Is.True);
            Assert.That(Ring(otherFile).IsVisible, Is.False);
            layoutTabs.SelectedIndex = 1 - layoutTabs.SelectedIndex;
            HeadlessTestHelpers.Render(2);

            if (fail)
                release.TrySetException(new IOException("Project opening failed."));
            else
                release.TrySetResult();
            await WaitForOpeningToEndAsync(view);
            HeadlessTestHelpers.Render();
            Assert.That(list.GetVisualDescendants().OfType<FAProgressRing>()
                .All(control => !control.IsVisible && !control.IsIndeterminate), Is.True);
        }
        finally
        {
            release.TrySetResult();
            try
            {
                await WaitForOpeningToEndAsync(view);
            }
            finally
            {
                TestShell.Project.Opening -= opening;
                window.Close();
                host.DataContext = null;
                HeadlessTestHelpers.Settle();
                await TestReset.ResetShellAsync();
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    // The grid leaves room around its frames, and a click there must not open the entry selected before.
    [AvaloniaTest]
    public async Task A_click_beside_the_entries_opens_nothing()
    {
        await TestReset.ResetShellAsync();
        string file = CreateProjectFiles("beside", 1)[0];
        int attempts = 0;
        Func<string, Task> opening = _ =>
        {
            attempts++;
            return Task.CompletedTask;
        };

        using var recent = new RecentProjectsScope(RecentProjectsLayout.Grid, file);
        var view = new EditorHostFallback();
        var host = new MainView { DataContext = TestShell.MainViewModel, Content = view };
        var window = new Window { Width = 1200, Height = 720 };
        TestShell.Project.Opening += opening;
        try
        {
            window.Show();
            window.Content = host;
            HeadlessTestHelpers.Render();
            ListBox list = RecentList(view);
            list.SelectedIndex = 0;
            HeadlessTestHelpers.Render();

            Point beside = list.TranslatePoint(new Point(list.Bounds.Width - 8, list.Bounds.Height - 8), window)!.Value;
            TimelineTestHost.WaitForHitTest(window, list, beside);
            window.MouseDown(beside, MouseButton.Left);
            window.MouseUp(beside, MouseButton.Left);

            HeadlessTestHelpers.Render();
            Assert.Multiple(() =>
            {
                Assert.That(attempts, Is.Zero);
                Assert.That(view.OpeningFile, Is.Null);
            });
        }
        finally
        {
            TestShell.Project.Opening -= opening;
            window.Close();
            host.DataContext = null;
            HeadlessTestHelpers.Settle();
            await TestReset.ResetShellAsync();
        }
    }

    // A press that drags off its entry, or a right click for the menu, is no request to open.
    [AvaloniaTest]
    public async Task Only_a_left_click_that_ends_on_its_entry_opens_it()
    {
        await TestReset.ResetShellAsync();
        string[] files = CreateProjectFiles("click-off", 2);
        int attempts = 0;
        Func<string, Task> opening = _ =>
        {
            attempts++;
            return Task.CompletedTask;
        };

        using var recent = new RecentProjectsScope(RecentProjectsLayout.Grid, files);
        var view = new EditorHostFallback();
        var host = new MainView { DataContext = TestShell.MainViewModel, Content = view };
        var window = new Window { Width = 1200, Height = 720 };
        TestShell.Project.Opening += opening;
        try
        {
            window.Show();
            window.Content = host;
            HeadlessTestHelpers.Render();
            ListBox list = RecentList(view);
            ListBoxItem first = Row(list, files[0]);
            ListBoxItem second = Row(list, files[1]);
            Point onFirst = HeadlessTestHelpers.Center(first, window);
            Point onSecond = HeadlessTestHelpers.Center(second, window);
            TimelineTestHost.WaitForHitTest(window, first, onFirst);
            TimelineTestHost.WaitForHitTest(window, second, onSecond);

            window.MouseDown(onFirst, MouseButton.Left);
            window.MouseMove(onSecond);
            window.MouseUp(onSecond, MouseButton.Left);
            window.MouseDown(onFirst, MouseButton.Right);
            window.MouseUp(onFirst, MouseButton.Right);
            HeadlessTestHelpers.Render();

            Assert.Multiple(() =>
            {
                Assert.That(attempts, Is.Zero);
                Assert.That(view.OpeningFile, Is.Null);
            });
        }
        finally
        {
            TestShell.Project.Opening -= opening;
            window.Close();
            host.DataContext = null;
            HeadlessTestHelpers.Settle();
            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Enter_opens_the_selected_entry()
    {
        await TestReset.ResetShellAsync();
        string file = CreateProjectFiles("enter", 1)[0];
        var started = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        Func<string, Task> opening = path =>
        {
            started.TrySetResult(path);
            return Task.CompletedTask;
        };

        using var recent = new RecentProjectsScope(RecentProjectsLayout.Grid, file);
        var view = new EditorHostFallback();
        var host = new MainView { DataContext = TestShell.MainViewModel, Content = view };
        var window = new Window { Width = 1200, Height = 720 };
        TestShell.Project.Opening += opening;
        try
        {
            window.Show();
            window.Content = host;
            HeadlessTestHelpers.Render();
            ListBox list = RecentList(view);
            list.SelectedIndex = 0;
            HeadlessTestHelpers.Render();

            list.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });

            Assert.That(await started.Task.WaitAsync(TimeSpan.FromSeconds(5)), Is.EqualTo(file));
            await WaitForOpeningToEndAsync(view);
        }
        finally
        {
            TestShell.Project.Opening -= opening;
            window.Close();
            host.DataContext = null;
            HeadlessTestHelpers.Settle();
            await TestReset.ResetShellAsync();
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
