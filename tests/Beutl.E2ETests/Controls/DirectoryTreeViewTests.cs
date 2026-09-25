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
