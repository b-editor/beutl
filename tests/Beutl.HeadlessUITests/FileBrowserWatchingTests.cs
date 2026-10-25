using System.Collections.Concurrent;
using System.Reactive.Linq;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Beutl.Editor.Components.FileBrowserTab.Services;
using Beutl.Editor.Components.FileBrowserTab.ViewModels;
using Beutl.Editor.Components.FileBrowserTab.Views;
using Beutl.Extensibility;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Moq;
using SkiaSharp;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class FileBrowserWatchingTests
{
    [AvaloniaTest]
    public async Task Watch_scope_follows_flat_tree_and_home_views()
    {
        string root = Directory.CreateTempSubdirectory("beutl-browser-watch-").FullName;
        string nested = Directory.CreateDirectory(Path.Combine(root, "Documents")).FullName;
        Directory.CreateDirectory(Path.Combine(root, "Pictures"));
        File.WriteAllText(Path.Combine(root, "notes.txt"), "notes");
        var started = new ConcurrentQueue<FileSystemWatcher>();
        var watcher = new DirectoryWatcherService(TimeSpan.FromMilliseconds(20),
            action => Dispatcher.UIThread.Post(action), instance =>
            {
                instance.EnableRaisingEvents = true;
                started.Enqueue(instance);
            });
        var context = new Mock<IEditorContext>();
        var scene = new Scene { Uri = new Uri(Path.Combine(root, "main.scene")) };
        var browser = new FileBrowserTabViewModel(context.Object, null, watcher);
        var window = new Window
        {
            Content = new FileBrowserTabView { DataContext = browser },
            Width = 480,
            Height = 400
        };
        try
        {
            browser.RootPath.Value = root;
            window.Show();
            await HeadlessTestHelpers.WaitUntilAsync(() => started.Count == 1);
            Assert.That(started.Last().IncludeSubdirectories, Is.False, "Icons only show the current directory.");
            FileSystemItemViewModel original = browser.Items.First();
            browser.SelectedItems.Add(original);
            File.WriteAllText(Path.Combine(nested, "nested.txt"), "nested");
            await Task.Delay(100);
            HeadlessTestHelpers.Render();
            Assert.That(browser.Items.First(), Is.SameAs(original), "Unshown descendant writes must not rebuild the icons.");
            Assert.That(browser.SelectedItems, Does.Contain(original));
            HeadlessCapture.SaveIfRequested(window, nameof(FileBrowserWatchingTests), "icons");

            string visible = Path.Combine(root, "new.txt");
            File.WriteAllText(visible, "new");
            await HeadlessTestHelpers.WaitUntilAsync(() => browser.Items.Any(item => item.FullPath == visible));

            browser.ViewMode.Value = FileBrowserViewMode.Tree;
            await HeadlessTestHelpers.WaitUntilAsync(() => started.Count == 2);
            Assert.That(started.Last().IncludeSubdirectories, Is.True);
            File.Delete(Path.Combine(nested, "nested.txt"));
            await HeadlessTestHelpers.WaitUntilAsync(() => browser.TreeRootItems
                .Single(item => item.FullPath == nested).Children!.Count == 0);
            HeadlessTestHelpers.Render();
            HeadlessCapture.SaveIfRequested(window, nameof(FileBrowserWatchingTests), "tree");

            browser.ViewMode.Value = FileBrowserViewMode.List;
            await HeadlessTestHelpers.WaitUntilAsync(() => started.Count == 3);
            Assert.That(started.Last().IncludeSubdirectories, Is.False);
            HeadlessTestHelpers.Render();
            HeadlessCapture.SaveIfRequested(window, nameof(FileBrowserWatchingTests), "list");

            context.Setup(x => x.GetService(typeof(Scene))).Returns(scene);
            browser.NavigateToHome();
            await HeadlessTestHelpers.WaitUntilAsync(() => started.Count == 4);
            Assert.That(started.Last().IncludeSubdirectories, Is.True, "Home media results include descendants.");
        }
        finally
        {
            window.Close();
            browser.Dispose();
            Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaTest]
    [TestCase(FileBrowserViewMode.Icon)]
    [TestCase(FileBrowserViewMode.List)]
    [TestCase(FileBrowserViewMode.Tree)]
    public async Task Startup_catches_up_file_details_and_thumbnails_without_replacing_items(FileBrowserViewMode mode)
    {
        string root = Directory.CreateTempSubdirectory("beutl-browser-details-").FullName;
        string file = Path.Combine(root, "notes.txt");
        string image = Path.Combine(root, "image.png");
        File.WriteAllText(file, "a");
        WriteImage(image, SKColors.Red);
        using var startup = new PausedWatcher();
        using var browser = new FileBrowserTabViewModel(new Mock<IEditorContext>().Object, null, startup.Service);
        try
        {
            browser.ViewMode.Value = mode;
            browser.RootPath.Value = root;
            await startup.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var items = mode == FileBrowserViewMode.Tree ? browser.TreeRootItems : browser.Items;
            var textItem = items.Single(item => item.FullPath == file);
            var imageItem = items.Single(item => item.FullPath == image);
            await HeadlessTestHelpers.WaitUntilAsync(() => imageItem.Thumbnail.Value is not null);
            var thumbnail = imageItem.Thumbnail.Value;
            browser.SelectedItems.Add(textItem);

            File.WriteAllText(file, new string('x', 4096));
            DateTime modified = File.GetLastWriteTimeUtc(image).AddSeconds(2);
            WriteImage(image, SKColors.Blue);
            File.SetLastWriteTimeUtc(image, modified);
            startup.Resume();
            await HeadlessTestHelpers.WaitUntilAsync(() => textItem.MediaInfoText.Value!.StartsWith("4 KB")
                && imageItem.Thumbnail.Value is not null && imageItem.Thumbnail.Value != thumbnail);
            Assert.That(items.Single(item => item.FullPath == file), Is.SameAs(textItem));
            Assert.That(items.Single(item => item.FullPath == image), Is.SameAs(imageItem));
            Assert.That(browser.SelectedItems, Does.Contain(textItem));
        }
        finally { startup.Resume(); browser.Dispose(); Directory.Delete(root, true); }
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Home_startup_keeps_selection_and_reconciles_media_results(bool changeMedia)
    {
        string root = Directory.CreateTempSubdirectory("beutl-browser-home-start-").FullName;
        string assets = Directory.CreateDirectory(Path.Combine(root, "assets")).FullName;
        string keep = Path.Combine(assets, "keep.png");
        string removed = Path.Combine(assets, "removed.png");
        string added = Path.Combine(assets, "added.png");
        WriteImage(keep, SKColors.Red);
        WriteImage(removed, SKColors.Blue);
        var context = new Mock<IEditorContext>();
        context.Setup(x => x.GetService(typeof(Scene))).Returns(new Scene { Uri = new Uri(Path.Combine(root, "main.scene")) });
        using var startup = new PausedWatcher();
        using var browser = new FileBrowserTabViewModel(context.Object, null, startup.Service);
        var window = new Window { Content = new FileBrowserTabView { DataContext = browser }, Width = 480, Height = 600 };
        try
        {
            await startup.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await HeadlessTestHelpers.WaitUntilAsync(() => !browser.IsLoadingMediaFiles.Value);
            var projectItems = browser.ProjectDirectoryItems.ToArray();
            var favorites = browser.FavoriteItems.ToArray();
            var mediaItems = browser.MediaFileItems.ToArray();
            var retained = mediaItems.Single(item => item.FullPath == keep);
            var items = changeMedia ? browser.MediaFileItems : browser.ProjectDirectoryItems;
            var selected = changeMedia ? retained : projectItems.Single();
            window.Show();
            HeadlessTestHelpers.Render();
            var list = window.GetVisualDescendants().OfType<ListBox>()
                .First(control => control.IsVisible && ReferenceEquals(control.ItemsSource, items));
            list.SelectedItem = selected;
            Assert.That(list.SelectedItem, Is.SameAs(selected));
            int rescans = 0;
            using var subscription = browser.IsLoadingMediaFiles.Subscribe(loading => { if (loading) rescans++; });
            if (changeMedia)
            {
                File.Delete(removed);
                WriteImage(added, SKColors.Green);
            }
            startup.Resume();
            await HeadlessTestHelpers.WaitUntilAsync(() => rescans > 0 && !browser.IsLoadingMediaFiles.Value);
            HeadlessTestHelpers.Render();
            Assert.That(browser.ProjectDirectoryItems, Is.EqualTo(projectItems));
            Assert.That(browser.FavoriteItems, Is.EqualTo(favorites));
            Assert.That(list.SelectedItem, Is.SameAs(selected));
            Assert.That(browser.SelectedItems, Does.Contain(selected));
            Assert.That(browser.MediaFileItems.Single(item => item.FullPath == keep), Is.SameAs(retained));
            if (changeMedia)
                Assert.That(browser.MediaFileItems.Select(item => item.FullPath), Is.EquivalentTo(new[] { keep, added }));
            else
                Assert.That(browser.MediaFileItems, Is.EqualTo(mediaItems));
            HeadlessCapture.SaveIfRequested(window, nameof(FileBrowserWatchingTests), $"home-startup-{changeMedia}");
        }
        finally { startup.Resume(); window.Close(); browser.Dispose(); Directory.Delete(root, true); }
    }

    [AvaloniaTest]
    public async Task Unreadable_directory_does_not_skip_catchup_of_its_readable_sibling()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("This test uses Unix directory permissions.");
            return;
        }
        string root = Directory.CreateTempSubdirectory("beutl-browser-unreadable-").FullName;
        string blocked = Directory.CreateDirectory(Path.Combine(root, "a-blocked")).FullName;
        string readable = Directory.CreateDirectory(Path.Combine(root, "b-readable")).FullName;
        string added = Path.Combine(readable, "arrived.txt");
        UnixFileMode permissions = File.GetUnixFileMode(blocked);
        using var startup = new PausedWatcher();
        using var browser = new FileBrowserTabViewModel(new Mock<IEditorContext>().Object, null, startup.Service);
        try
        {
            File.SetUnixFileMode(blocked, UnixFileMode.None);
            try
            {
                new DirectoryInfo(blocked).EnumerateFileSystemInfos().Any();
                Assert.Ignore("This account can read directories without permission bits.");
            }
            catch (UnauthorizedAccessException) { }
            browser.ViewMode.Value = FileBrowserViewMode.Tree;
            browser.RootPath.Value = root;
            await startup.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var sibling = browser.TreeRootItems.Single(item => item.FullPath == readable);
            sibling.IsExpanded.Value = true;
            Assert.That(sibling.Children, Is.Empty);
            File.WriteAllText(added, "arrived");
            startup.Resume();
            await HeadlessTestHelpers.WaitUntilAsync(() => sibling.Children!.Any(item => item.FullPath == added));
            Assert.That(browser.TreeRootItems.Single(item => item.FullPath == readable), Is.SameAs(sibling));
            Assert.That(sibling.IsExpanded.Value, Is.True);
        }
        finally
        {
            startup.Resume();
            File.SetUnixFileMode(blocked, permissions);
            browser.Dispose();
            Directory.Delete(root, true);
        }
    }

    private static void WriteImage(string path, SKColor color)
    {
        using var bitmap = new SKBitmap(2, 2);
        bitmap.Erase(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
    }

    private sealed class PausedWatcher : IDisposable
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public DirectoryWatcherService Service { get; }

        public PausedWatcher()
        {
            Service = new DirectoryWatcherService(TimeSpan.Zero, action => Dispatcher.UIThread.Post(action), watcher =>
            {
                Entered.TrySetResult();
                if (!_release.Task.Wait(TimeSpan.FromSeconds(10)))
                    throw new TimeoutException("The test did not release watcher startup.");
                watcher.EnableRaisingEvents = true;
            });
        }

        public void Resume() => _release.TrySetResult();
        public void Dispose() { Resume(); Service.Dispose(); }
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Slow_start_keeps_the_UI_responsive_and_cannot_restore_an_old_folder(bool close)
    {
        string root = Directory.CreateTempSubdirectory("beutl-browser-slow-watch-").FullName;
        string next = Directory.CreateDirectory(Path.Combine(root, "Pictures")).FullName;
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new ConcurrentQueue<FileSystemWatcher>();
        var watcher = new DirectoryWatcherService(TimeSpan.Zero,
            action => Dispatcher.UIThread.Post(action), instance =>
            {
                started.Enqueue(instance);
                if (started.Count == 1)
                {
                    entered.SetResult();
                    try
                    {
                        if (!release.Wait(TimeSpan.FromSeconds(5)))
                            throw new TimeoutException("The UI did not release watcher startup.");
                        instance.EnableRaisingEvents = true;
                    }
                    finally { finished.SetResult(); }
                }
                else
                {
                    instance.EnableRaisingEvents = true;
                }
            });
        var browser = new FileBrowserTabViewModel(new Mock<IEditorContext>().Object, null, watcher);
        var window = new Window
        {
            Content = new FileBrowserTabView { DataContext = browser },
            Width = 480,
            Height = 400
        };
        try
        {
            browser.RootPath.Value = root;
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            window.Show();
            HeadlessTestHelpers.Render();
            Assert.That(finished.Task.IsCompleted, Is.False, "Rendering must run while watcher startup is still blocked.");
            browser.RootPath.Value = next;
            if (close) browser.Dispose();
            Assert.That(finished.Task.IsCompleted, Is.False, "Navigation and closing must not wait for startup either.");

            // This change precedes startup of the destination watcher and must be caught up.
            string added = Path.Combine(next, "arrived.txt");
            File.WriteAllText(added, "arrived");
            release.Set();
            await finished.Task;
            if (close)
            {
                await watcher.WatchAsync(next);
                Assert.That(watcher.IsWatching, Is.False);
                await HeadlessTestHelpers.WaitUntilAsync(() => !started.First().EnableRaisingEvents);
                Assert.That(started, Has.Count.EqualTo(1));
            }
            else
            {
                await HeadlessTestHelpers.WaitUntilAsync(() => browser.Items.Any(item => item.FullPath == added));
                Assert.That(started.Last().Path, Is.EqualTo(FilePathComparison.ResolveCanonicalPath(next)));
                Assert.That(browser.RootPath.Value, Is.EqualTo(next));
            }
        }
        finally
        {
            release.Set();
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(10));
            window.Close();
            browser.Dispose();
            Directory.Delete(root, recursive: true);
        }
    }
}
