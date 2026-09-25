using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Beutl.Controls;
using Beutl.Testing.Headless;

namespace Beutl.E2ETests.Controls;

[TestFixture]
public class DirectoryTreeViewTests
{
    [AvaloniaTest]
    public async Task Root_tracks_created_renamed_and_deleted_files_without_a_context_factory()
    {
        string root = Path.Combine(Path.GetTempPath(), $"beutl-tree-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string folder = Path.Combine(root, "folder");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(root, "seed.txt"), "seed");

        using var watcher = new FileSystemWatcher(root) { EnableRaisingEvents = true };
        var tree = new DirectoryTreeView(watcher);
        var window = new Window { Width = 320, Height = 240, Content = tree };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            tree.Sort();

            TreeViewItem[] initial = tree.ItemsSource!.Cast<TreeViewItem>().ToArray();
            Assert.That(initial[0], Is.TypeOf<DirectoryTreeItem>());
            Assert.That(initial.OfType<FileTreeItem>().Any(item => item.Info.Name == "seed.txt"), Is.True);

            string created = Path.Combine(root, "new.txt");
            File.WriteAllText(created, "new");
            await WaitUntil(() => tree.ItemsSource!.Cast<TreeViewItem>()
                .OfType<FileTreeItem>().Any(item => item.Info.Name == "new.txt"));

            string renamed = Path.Combine(root, "renamed.txt");
            File.Move(created, renamed);
            await WaitUntil(() => tree.ItemsSource!.Cast<TreeViewItem>()
                .OfType<FileTreeItem>().Any(item => item.Info.Name == "renamed.txt"));

            File.Delete(renamed);
            await WaitUntil(() => tree.ItemsSource!.Cast<TreeViewItem>()
                .OfType<FileTreeItem>().All(item => item.Info.Name != "renamed.txt"));
        }
        finally
        {
            watcher.EnableRaisingEvents = false;
            HeadlessTestHelpers.Settle();
            window.Close();
            Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaTest]
    public void Null_rename_text_restores_file_and_folder_names()
    {
        string root = Path.Combine(Path.GetTempPath(), $"beutl-tree-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string filePath = Path.Combine(root, "seed.txt");
        string folderPath = Path.Combine(root, "folder");
        File.WriteAllText(filePath, "seed");
        Directory.CreateDirectory(folderPath);

        using var watcher = new FileSystemWatcher(root);
        try
        {
            var file = new FileTreeItem(new FileInfo(filePath));
            file.StartRename();
            ((TextBox)file.Header!).Text = null;
            file.EndRename();
            Assert.That(file.Header, Is.EqualTo("seed.txt"));
            Assert.That(File.Exists(filePath), Is.True);

            var folder = new DirectoryTreeItem(new DirectoryInfo(folderPath), watcher);
            folder.StartRename();
            ((TextBox)folder.Header!).Text = null;
            folder.EndRename();
            Assert.That(folder.Header, Is.EqualTo("folder"));
            Assert.That(Directory.Exists(folderPath), Is.True);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaTest]
    public void Case_only_rename_updates_file_and_folder_names()
    {
        string root = Path.Combine(Path.GetTempPath(), $"beutl-tree-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string filePath = Path.Combine(root, "Foo.txt");
        string folderPath = Path.Combine(root, "Folder");
        File.WriteAllText(filePath, "seed");
        Directory.CreateDirectory(folderPath);

        using var watcher = new FileSystemWatcher(root);
        try
        {
            var file = new FileTreeItem(new FileInfo(filePath));
            file.StartRename();
            ((TextBox)file.Header!).Text = "foo.txt";
            file.EndRename();
            Assert.That(file.Info.Name, Is.EqualTo("foo.txt"));
            Assert.That(Directory.GetFiles(root).Select(Path.GetFileName), Does.Contain("foo.txt"));

            var folder = new DirectoryTreeItem(new DirectoryInfo(folderPath), watcher);
            folder.StartRename();
            ((TextBox)folder.Header!).Text = "folder";
            folder.EndRename();
            Assert.That(folder.Info.Name, Is.EqualTo("folder"));
            Assert.That(Directory.GetDirectories(root).Select(Path.GetFileName), Does.Contain("folder"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void Case_variant_destination_is_a_conflict_only_when_it_is_a_distinct_entry()
    {
        string root = Path.Combine(Path.GetTempPath(), $"beutl-tree-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            string file = Path.Combine(root, "Foo.txt");
            string caseVariantFile = Path.Combine(root, "foo.txt");
            File.WriteAllText(file, "first");
            bool fileSystemDistinguishesFiles = !File.Exists(caseVariantFile);
            if (fileSystemDistinguishesFiles)
                File.WriteAllText(caseVariantFile, "second");
            Assert.That(DirectoryTreeRename.HasDistinctDestination(file, caseVariantFile),
                Is.EqualTo(fileSystemDistinguishesFiles));
            string otherFile = Path.Combine(root, "other.txt");
            File.WriteAllText(otherFile, "other");
            Assert.That(DirectoryTreeRename.HasDistinctDestination(file, otherFile), Is.True);

            string folder = Path.Combine(root, "Folder");
            string caseVariantFolder = Path.Combine(root, "folder");
            Directory.CreateDirectory(folder);
            bool fileSystemDistinguishesFolders = !Directory.Exists(caseVariantFolder);
            if (fileSystemDistinguishesFolders)
                Directory.CreateDirectory(caseVariantFolder);
            Assert.That(DirectoryTreeRename.HasDistinctDestination(folder, caseVariantFolder),
                Is.EqualTo(fileSystemDistinguishesFolders));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaTest]
    public void Sort_tolerates_null_text_and_nonstring_headers()
    {
        string root = Path.Combine(Path.GetTempPath(), $"beutl-tree-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string folderPath = Path.Combine(root, "folder");
        Directory.CreateDirectory(folderPath);
        File.WriteAllText(Path.Combine(root, "root.txt"), "root");
        File.WriteAllText(Path.Combine(folderPath, "nested.txt"), "nested");

        using var watcher = new FileSystemWatcher(root);
        try
        {
            var tree = new DirectoryTreeView(watcher);
            var items = tree.ItemsSource!.Cast<TreeViewItem>().ToArray();
            var file = items.OfType<FileTreeItem>().Single();
            var folder = items.OfType<DirectoryTreeItem>().Single();

            file.Header = new TextBlock { Text = null };
            folder.Header = new TextBlock { Text = null };
            Assert.DoesNotThrow(tree.Sort);
            file.Header = new Border();
            folder.Header = new Border();
            Assert.DoesNotThrow(tree.Sort);

            folder.IsExpanded = true;
            HeadlessTestHelpers.Settle();
            var nested = folder.ItemsSource!.Cast<TreeViewItem>().OfType<FileTreeItem>().Single();
            nested.Header = new TextBlock { Text = null };
            Assert.DoesNotThrow(folder.Sort);
            nested.Header = new Border();
            Assert.DoesNotThrow(folder.Sort);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 250; attempt++)
        {
            HeadlessTestHelpers.Settle();
            if (condition()) return;
            await Task.Delay(20);
        }

        Assert.Fail("The file watcher did not update the tree.");
    }
}
