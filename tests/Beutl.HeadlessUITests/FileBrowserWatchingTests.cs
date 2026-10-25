using System.Collections.Concurrent;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Threading;
using Beutl.Editor.Components.FileBrowserTab.Services;
using Beutl.Editor.Components.FileBrowserTab.ViewModels;
using Beutl.Editor.Components.FileBrowserTab.Views;
using Beutl.Extensibility;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Moq;

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
