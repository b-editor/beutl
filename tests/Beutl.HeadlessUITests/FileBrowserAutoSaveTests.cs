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
    public async Task Moving_a_sidecar_named_directory_out_of_the_project_removes_home_media_results()
    {
        string scratch = Path.Combine(Path.GetTempPath(), $"beutl-browser-media-move-{Guid.NewGuid():N}");
        string root = Path.Combine(scratch, "project");
        string assets = Path.Combine(root, "assets");
        string directory = Path.Combine(assets, $"clip.belm.{Guid.NewGuid():N}.tmp");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(assets, "keep.txt"), "keep");
        string image = Path.Combine(directory, "image.png");
        File.WriteAllBytes(image, Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+iWZkAAAAASUVORK5CYII="));
        var context = new Mock<IEditorContext>();
        context.Setup(x => x.GetService(typeof(Scene)))
            .Returns(new Scene { Uri = new Uri(Path.Combine(root, "main.scene")) });
        using var browser = new FileBrowserTabViewModel(context.Object);
        try
        {
            await WaitUntilAsync(() => browser.MediaFileItems.Any(item => item.FullPath == image));
            Directory.Move(directory, Path.Combine(scratch, "moved"));
            await WaitUntilAsync(() => !browser.IsLoadingMediaFiles.Value && browser.MediaFileItems.Count == 0);
        }
        finally
        {
            browser.Dispose();
            Directory.Delete(scratch, recursive: true);
        }
    }

    [AvaloniaTest]
    public async Task Sidecar_named_directories_follow_create_rename_and_delete_notifications()
    {
        string root = Path.Combine(Path.GetTempPath(), $"beutl-browser-directories-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string original = Path.Combine(root, $"clip.belm.{Guid.NewGuid():N}.tmp");
        string renamed = Path.Combine(root, $"clip.belm.{Guid.NewGuid():N}.tmp");
        // Seed an existing directory too: its deletion cannot be classified with Directory.Exists.
        Directory.CreateDirectory(original);
        File.WriteAllText(Path.Combine(original, "nested.belm"), "{}");
        using var browser = new FileBrowserTabViewModel(new Mock<IEditorContext>().Object);
        try
        {
            browser.RootPath.Value = root;
            Assert.That(browser.Items.Single().IsDirectory, Is.True);
            Directory.Delete(original, recursive: true);
            await WaitUntilAsync(() => browser.Items.Count == 0);

            Directory.CreateDirectory(original);
            await WaitUntilAsync(() => browser.Items.Any(x => x.FullPath == original && x.IsDirectory));
            Directory.Move(original, renamed);
            await WaitUntilAsync(() => browser.Items.Count == 1 && browser.Items[0].FullPath == renamed);
            Directory.Delete(renamed);
            await WaitUntilAsync(() => browser.Items.Count == 0);
        }
        finally
        {
            browser.Dispose();
            Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaTest]
    public async Task First_document_save_enables_a_collapsed_folder_without_resetting_the_listing()
    {
        string root = Path.Combine(Path.GetTempPath(), $"beutl-browser-first-save-{Guid.NewGuid():N}");
        string folderPath = Path.Combine(root, "Scene");
        Directory.CreateDirectory(folderPath);
        string document = Path.Combine(folderPath, "first.belm");
        string temporary = $"{document}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporary, "{}");
        using var browser = new FileBrowserTabViewModel(new Mock<IEditorContext>().Object);
        var window = new Window
        {
            Content = new FileBrowserTabView { DataContext = browser },
            Width = 480,
            Height = 400
        };
        try
        {
            browser.ViewMode.Value = FileBrowserViewMode.Tree;
            browser.RootPath.Value = root;
            window.Show();
            HeadlessTestHelpers.Render();
            FileSystemItemViewModel folder = browser.TreeRootItems.Single();
            Assert.That(folder.Children, Is.Empty);
            File.Move(temporary, document);
            await WaitUntilAsync(() => folder.Children!.Count > 0);
            Assert.That(browser.TreeRootItems.Single(), Is.SameAs(folder));
            folder.IsExpanded.Value = true;
            HeadlessTestHelpers.Render();
            Assert.That(folder.Children!.Single().FullPath, Is.EqualTo(document));

            if (Environment.GetEnvironmentVariable("BEUTL_FILE_BROWSER_AUTOSAVE_CAPTURE") is { Length: > 0 } capture)
            {
                using var frame = window.CaptureRenderedFrame();
                Assert.That(frame, Is.Not.Null);
                frame!.Save(Path.ChangeExtension(capture, "first-save.png"), PngBitmapEncoderOptions.Default);
            }

            folder.IsExpanded.Value = false;
            File.Delete(document);
            await WaitUntilAsync(() => folder.Children!.Count == 0);
            Assert.That(browser.TreeRootItems.Single(), Is.SameAs(folder));
        }
        finally
        {
            window.Close();
            browser.Dispose();
            Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaTest]
    public void Collapsed_folders_only_offer_expansion_for_visible_entries()
    {
        string root = Path.Combine(Path.GetTempPath(), $"beutl-browser-expand-{Guid.NewGuid():N}");
        string saving = Path.Combine(root, "Saving only");
        string ordinary = Path.Combine(root, "Ordinary file");
        string directory = Path.Combine(root, "Subfolder");
        Directory.CreateDirectory(saving);
        Directory.CreateDirectory(ordinary);
        Directory.CreateDirectory(directory);
        string temporaryName = $"clip.belm.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(Path.Combine(saving, temporaryName), "{}");
        File.WriteAllText(Path.Combine(ordinary, "notes.tmp"), "{}");
        Directory.CreateDirectory(Path.Combine(directory, temporaryName));
        using var browser = new FileBrowserTabViewModel(new Mock<IEditorContext>().Object);
        var window = new Window
        {
            Content = new FileBrowserTabView { DataContext = browser },
            Width = 480,
            Height = 400
        };
        try
        {
            browser.ViewMode.Value = FileBrowserViewMode.Tree;
            browser.RootPath.Value = root;
            window.Show();
            HeadlessTestHelpers.Render();
            FileSystemItemViewModel empty = browser.TreeRootItems.Single(x => x.FullPath == saving);
            FileSystemItemViewModel file = browser.TreeRootItems.Single(x => x.FullPath == ordinary);
            FileSystemItemViewModel folder = browser.TreeRootItems.Single(x => x.FullPath == directory);
            Assert.Multiple(() =>
            {
                Assert.That(empty.Children, Is.Empty, "Ignored autosave files must not create an expand arrow.");
                Assert.That(file.Children, Has.Count.EqualTo(1));
                Assert.That(folder.Children, Has.Count.EqualTo(1), "Directories with sidecar-like names stay visible.");
            });

            empty.Refresh();
            Assert.That(empty.Children, Is.Empty, "Refreshing a collapsed folder must use the same filter.");
            file.LoadChildren();
            folder.LoadChildren();
            Assert.That(file.Children!.Single().Name.Value, Is.EqualTo("notes.tmp"));
            Assert.That(folder.Children!.Single().IsDirectory, Is.True);

            if (Environment.GetEnvironmentVariable("BEUTL_FILE_BROWSER_AUTOSAVE_CAPTURE") is { Length: > 0 } capture)
            {
                HeadlessTestHelpers.Render();
                using var frame = window.CaptureRenderedFrame();
                Assert.That(frame, Is.Not.Null);
                frame!.Save(Path.ChangeExtension(capture, "expanders.png"), PngBitmapEncoderOptions.Default);
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
        string[] visibleNames =
        [
            .. documents, "notes.tmp", "clip.belm.backup.tmp",
            "clip.belm. 0123456789abcdef0123456789abcdef.tmp",
            "clip.belm.0123456789abcdef0123456789abcdef .tmp"
        ];
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
        bool favoriteAdded = false;
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
            browser.ToggleFavorite(element.Uri!.LocalPath);
            favoriteAdded = true;
            browser.NavigateToHome();
            await SettleWatcherAsync();
            FileSystemItemViewModel[] homeItems = browser.ProjectDirectoryItems.ToArray();
            FileSystemItemViewModel[] favoriteItems = browser.FavoriteItems.ToArray();
            element.ZIndex++;
            autoSave.SaveObjects([element, scene]);
            await SettleWatcherAsync();
            Assert.That(browser.ProjectDirectoryItems, Is.EqualTo(homeItems));
            Assert.That(browser.FavoriteItems, Is.EqualTo(favoriteItems));

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
            if (favoriteAdded) browser.ToggleFavorite(element.Uri!.LocalPath);
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
