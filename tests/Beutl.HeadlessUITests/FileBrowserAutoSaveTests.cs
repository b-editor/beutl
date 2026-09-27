using System.Collections.Specialized;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media.Imaging;
using Beutl.Editor;
using Beutl.Editor.Components.FileBrowserTab.ViewModels;
using Beutl.Editor.Components.FileBrowserTab.Views;
using Beutl.Extensibility;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Moq;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class FileBrowserAutoSaveTests
{
    [AvaloniaTest]
    [TestCase(FileBrowserViewMode.List, false)]
    [TestCase(FileBrowserViewMode.Icon, false)]
    [TestCase(FileBrowserViewMode.Tree, false)]
    [TestCase(FileBrowserViewMode.List, true)]
    public async Task Opening_or_refreshing_during_an_atomic_save_does_not_leave_temporary_items(
        FileBrowserViewMode mode, bool home)
    {
        string root = Path.Combine(Path.GetTempPath(), $"beutl-browser-saving-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string[] documents = ["project.bep", "main.scene", "clip.belm"];
        string[] visibleNames = [.. documents, "notes.tmp", "clip.belm.backup.tmp"];
        foreach (string name in visibleNames)
            File.WriteAllText(Path.Combine(root, name), "{}");

        // Pause each atomic save between writing its temporary file and replacing the document.
        string[] temporaryPaths = documents.Select(name => Path.Combine(root, $"{name}.{Guid.NewGuid():N}.tmp")).ToArray();
        foreach (string path in temporaryPaths)
            File.WriteAllText(path, "{}");

        var scene = new Scene { Uri = new Uri(Path.Combine(root, "main.scene")) };
        var context = new Mock<IEditorContext>();
        context.Setup(x => x.GetService(typeof(Scene))).Returns(scene);
        using var browser = new FileBrowserTabViewModel(context.Object);
        var window = new Window
        {
            Content = new FileBrowserTabView { DataContext = browser },
            Width = 480,
            Height = 600
        };
        try
        {
            browser.ViewMode.Value = mode;
            if (!home) browser.RootPath.Value = root;
            var items = home ? browser.ProjectDirectoryItems
                : mode == FileBrowserViewMode.Tree ? browser.TreeRootItems : browser.Items;
            window.Show();
            HeadlessTestHelpers.Render();
            Assert.That(items.Select(x => x.Name.Value), Is.EquivalentTo(visibleNames));

            browser.Refresh();
            Assert.That(items.Select(x => x.Name.Value), Is.EquivalentTo(visibleNames));

            // Expanding a folder uses the same enumeration path as the top-level listing.
            using var folder = new FileSystemItemViewModel(root, isDirectory: true);
            folder.LoadChildren();
            Assert.That(folder.Children!.Select(x => x.Name.Value), Is.EquivalentTo(visibleNames));

            FileSystemItemViewModel[] beforeMove = items.ToArray();
            for (int i = 0; i < documents.Length; i++)
                File.Move(temporaryPaths[i], Path.Combine(root, documents[i]), overwrite: true);
            await SettleWatcherAsync();
            Assert.That(items, Is.EqualTo(beforeMove), "Completing autosaves must not require a cleanup refresh.");

            if (home && Environment.GetEnvironmentVariable("BEUTL_FILE_BROWSER_AUTOSAVE_CAPTURE") is { Length: > 0 } capture)
            {
                using var frame = window.CaptureRenderedFrame();
                Assert.That(frame, Is.Not.Null);
                frame!.Save(Path.ChangeExtension(capture, "enumeration.png"), PngBitmapEncoderOptions.Default);
            }
        }
        finally
        {
            window.Close();
            browser.Dispose();
            Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaTest]
    public async Task Auto_saving_edits_preserves_browser_items_and_selection()
    {
        string root = Path.Combine(Path.GetTempPath(), $"beutl-browser-autosave-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var scene = new Scene { Uri = new Uri(Path.Combine(root, "main.scene")) };
        var element = new Element
        {
            Uri = new Uri(Path.Combine(root, "clip.belm")),
            Name = "Clip",
            Length = TimeSpan.FromSeconds(3)
        };
        scene.Children.Add(element);
        using var autoSave = new AutoSaveService();
        autoSave.SaveObjects([element, scene]);
        string asset = Path.Combine(root, "asset.txt");
        File.WriteAllText(asset, "asset");

        var context = new Mock<IEditorContext>();
        context.Setup(x => x.GetService(typeof(Scene))).Returns(scene);
        using var browser = new FileBrowserTabViewModel(context.Object);
        var view = new FileBrowserTabView { DataContext = browser };
        var window = new Window { Content = view, Width = 480, Height = 600 };
        try
        {
            browser.ViewMode.Value = FileBrowserViewMode.List;
            browser.RootPath.Value = root;
            window.Show();
            await SettleWatcherAsync();
            FileSystemItemViewModel selected = browser.Items.Single(x => x.FullPath == asset);
            browser.SelectedItems.Add(selected);
            int resets = 0;
            browser.Items.CollectionChanged += (_, e) =>
            {
                if (e.Action == NotifyCollectionChangedAction.Reset) resets++;
            };

            var stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < 3; i++)
            {
                element.ZIndex++;
                autoSave.SaveObjects([element, scene]);
                await SettleWatcherAsync();
            }
            TestContext.Out.WriteLine($"Three auto-saves: {resets} browser resets ({stopwatch.ElapsedMilliseconds} ms including watcher waits).");

            Assert.Multiple(() =>
            {
                Assert.That(resets, Is.Zero, "Editing must not rebuild the file browser after each auto-save.");
                Assert.That(browser.Items.Single(x => x.FullPath == asset), Is.SameAs(selected));
                Assert.That(browser.SelectedItems, Does.Contain(selected));
            });

            // Real file operations still refresh the listing.
            string added = Path.Combine(root, "imported.txt");
            File.WriteAllText(added, "imported");
            await WaitUntilAsync(() => browser.Items.Any(x => x.FullPath == added));
            File.Delete(added);
            await WaitUntilAsync(() => browser.Items.All(x => x.FullPath != added));

            // The default home view must also retain its project listing across edits.
            browser.NavigateToHome();
            await SettleWatcherAsync();
            FileSystemItemViewModel[] homeItems = browser.ProjectDirectoryItems.ToArray();
            element.ZIndex++;
            autoSave.SaveObjects([element, scene]);
            await SettleWatcherAsync();
            Assert.That(browser.ProjectDirectoryItems, Is.EqualTo(homeItems));

            if (Environment.GetEnvironmentVariable("BEUTL_FILE_BROWSER_AUTOSAVE_CAPTURE") is { Length: > 0 } capture)
            {
                HeadlessTestHelpers.Render();
                using var frame = window.CaptureRenderedFrame();
                Assert.That(frame, Is.Not.Null);
                frame!.Save(capture, PngBitmapEncoderOptions.Default);
            }
        }
        finally
        {
            window.Close();
            browser.Dispose();
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task SettleWatcherAsync()
    {
        // Allows native event delivery and the browser's 300 ms debounce to complete.
        await Task.Delay(1000);
        HeadlessTestHelpers.Render();
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var timeout = Stopwatch.StartNew();
        while (!condition() && timeout.Elapsed < TimeSpan.FromSeconds(5))
        {
            await Task.Delay(50);
            HeadlessTestHelpers.Render();
        }
        Assert.That(condition(), Is.True, "The file browser did not receive the filesystem change.");
    }
}
