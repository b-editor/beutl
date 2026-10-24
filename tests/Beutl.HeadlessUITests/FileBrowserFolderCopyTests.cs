using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Editor.Components.FileBrowserTab.ViewModels;
using Beutl.Language;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using FluentAvalonia.UI.Controls;

namespace Beutl.HeadlessUITests;

// A copied folder that meets one of the same name asks what to do, no folder link on either side is followed, and a
// copy never lands inside its own source (#2947).
[TestFixture]
public sealed class FileBrowserFolderCopyTests
{
    [AvaloniaTest]
    public async Task Merging_into_a_partial_copy_completes_it_without_overwriting()
    {
        (FileBrowserTabViewModel browser, string footage, string target) = await OpenAsync("merge");
        using var _ = browser;
        string partial = Directory.CreateDirectory(Path.Combine(target, "footage")).FullName;
        File.WriteAllText(Path.Combine(partial, "a.txt"), "old a");
        var shown = Answer(browser, FAContentDialogResult.Primary);

        await browser.TransferFilesToDirectoryAsync([(footage, true)], target, move: false);

        Assert.Multiple(() =>
        {
            Assert.That(shown.Select(dialog => dialog.Content), Is.EqualTo(new[] { string.Format(Strings.FileBrowser_FolderExists, "footage") }));
            Assert.That(File.ReadAllText(Path.Combine(partial, "a.txt")), Is.EqualTo("old a"));
            Assert.That(File.ReadAllText(Path.Combine(partial, "b.txt")), Is.EqualTo("b"));
        });
    }

    [AvaloniaTest]
    public async Task Skipping_leaves_the_existing_folder_and_copies_the_rest()
    {
        (FileBrowserTabViewModel browser, string footage, string target) = await OpenAsync("skip");
        using var _ = browser;
        string existing = Directory.CreateDirectory(Path.Combine(target, "footage")).FullName;
        string note = WriteFile(Path.GetDirectoryName(footage)!, "note.txt", "note");
        var shown = Answer(browser, FAContentDialogResult.Secondary);

        await browser.TransferFilesToDirectoryAsync([(footage, true), (note, false)], target, move: false);

        Assert.Multiple(() =>
        {
            Assert.That(shown, Has.Count.EqualTo(1));
            Assert.That(Directory.EnumerateFileSystemEntries(existing), Is.Empty);
            Assert.That(File.Exists(Path.Combine(target, "note.txt")), Is.True);
        });
    }

    [AvaloniaTest]
    public async Task Cancelling_copies_nothing()
    {
        (FileBrowserTabViewModel browser, string footage, string target) = await OpenAsync("cancel");
        using var _ = browser;
        string existing = Directory.CreateDirectory(Path.Combine(target, "footage")).FullName;
        string note = WriteFile(Path.GetDirectoryName(footage)!, "note.txt", "note");
        var shown = Answer(browser, FAContentDialogResult.None);

        await browser.TransferFilesToDirectoryAsync([(note, false), (footage, true)], target, move: false);

        Assert.Multiple(() =>
        {
            Assert.That(shown, Has.Count.EqualTo(1));
            Assert.That(Directory.EnumerateFileSystemEntries(existing), Is.Empty);
            Assert.That(File.Exists(Path.Combine(target, "note.txt")), Is.False);
        });
    }

    [AvaloniaTest]
    public async Task A_folder_link_in_the_copy_is_skipped_and_reported()
    {
        IgnoreUnlessLinksCanBeCreated();
        (FileBrowserTabViewModel browser, string footage, string target) = await OpenAsync("link");
        using var _ = browser;
        string elsewhere = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(target)!, "elsewhere")).FullName;
        WriteFile(elsewhere, "far.txt", "far");
        Directory.CreateSymbolicLink(Path.Combine(footage, "linked"), elsewhere);
        using var notifications = new NotificationCapture();

        await browser.TransferFilesToDirectoryAsync([(footage, true)], target, move: false);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(target, "footage", "b.txt")), Is.True);
            Assert.That(Path.Exists(Path.Combine(target, "footage", "linked")), Is.False);
            Assert.That(notifications.Single().Message, Is.EqualTo(MessageStrings.OperationFailed));
        });
    }

    [AvaloniaTest]
    public async Task A_link_to_the_folders_parent_does_not_nest()
    {
        IgnoreUnlessLinksCanBeCreated();
        (FileBrowserTabViewModel browser, string footage, string target) = await OpenAsync("loop");
        using var _ = browser;
        Directory.CreateSymbolicLink(Path.Combine(footage, "loop"), "..");
        using var notifications = new NotificationCapture();

        await browser.TransferFilesToDirectoryAsync([(footage, true)], target, move: false);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(target, "footage", "b.txt")), Is.True);
            Assert.That(Path.Exists(Path.Combine(target, "footage", "loop")), Is.False);
            Assert.That(notifications.Single().Message, Is.EqualTo(MessageStrings.OperationFailed));
        });
    }

    [AvaloniaTest]
    public async Task A_drop_target_that_links_into_the_source_is_refused()
    {
        IgnoreUnlessLinksCanBeCreated();
        (FileBrowserTabViewModel browser, string footage, string target) = await OpenAsync("into");
        using var _ = browser;
        string sub = Directory.CreateDirectory(Path.Combine(footage, "sub")).FullName;
        string intoSource = Path.Combine(target, "into-source");
        Directory.CreateSymbolicLink(intoSource, sub);
        using var notifications = new NotificationCapture();

        await browser.TransferFilesToDirectoryAsync([(footage, true)], intoSource, move: false);

        Assert.Multiple(() =>
        {
            Assert.That(Directory.EnumerateFileSystemEntries(sub), Is.Empty, "nothing nests inside the source");
            Assert.That(notifications.Single().Message, Is.EqualTo(MessageStrings.OperationFailed));
        });
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Merging_writes_nothing_through_a_destination_link(bool linkedSubfolder)
    {
        IgnoreUnlessLinksCanBeCreated();
        (FileBrowserTabViewModel browser, string footage, string target) = await OpenAsync(linkedSubfolder ? "sub-link" : "dest-link");
        using var _ = browser;
        // Stands for a folder outside the project, such as ~/.ssh.
        string outside = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(target)!, "outside")).FullName;
        string destination = Path.Combine(target, "footage");
        if (linkedSubfolder)
        {
            WriteFile(Directory.CreateDirectory(Path.Combine(footage, "sub")).FullName, "c.txt", "c");
            Directory.CreateDirectory(destination);
            Directory.CreateSymbolicLink(Path.Combine(destination, "sub"), outside);
        }
        else
        {
            Directory.CreateSymbolicLink(destination, outside);
        }

        var shown = Answer(browser, FAContentDialogResult.Primary);
        using var notifications = new NotificationCapture();

        await browser.TransferFilesToDirectoryAsync([(footage, true)], target, move: false);

        Assert.Multiple(() =>
        {
            Assert.That(shown, Has.Count.EqualTo(1));
            Assert.That(Directory.EnumerateFileSystemEntries(outside), Is.Empty);
            Assert.That(File.Exists(Path.Combine(destination, "b.txt")), Is.EqualTo(linkedSubfolder), "a real folder still merges");
            Assert.That(notifications.Single().Message, Is.EqualTo(MessageStrings.OperationFailed));
        });
    }

    [AvaloniaTest]
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task A_destination_with_a_linked_parent_is_skipped_and_reported(bool nestedTarget, bool existingDestination)
    {
        IgnoreUnlessLinksCanBeCreated();
        (FileBrowserTabViewModel browser, string footage, string target) = await OpenAsync($"parent-link-{nestedTarget}-{existingDestination}");
        using var _ = browser;
        string outside = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(target)!, "outside")).FullName;
        string outsideTarget = nestedTarget ? Directory.CreateDirectory(Path.Combine(outside, "nested")).FullName : outside;
        if (existingDestination)
        {
            WriteFile(Directory.CreateDirectory(Path.Combine(outsideTarget, "footage")).FullName, "a.txt", "old a");
        }

        string link = Path.Combine(target, "link");
        Directory.CreateSymbolicLink(link, Path.GetRelativePath(target, outside));
        string dropTarget = nestedTarget ? Path.Combine(link, "nested") : link;
        string[] originalEntries = Directory.GetFileSystemEntries(outside, "*", SearchOption.AllDirectories);
        int copies = 0;
        browser.CopyFile = (source, destination) =>
        {
            copies++;
            File.Copy(source, destination);
        };
        var shown = Answer(browser, FAContentDialogResult.Primary);
        using var notifications = new NotificationCapture();

        await browser.TransferFilesToDirectoryAsync([(footage, true)], dropTarget, move: false);

        Assert.Multiple(() =>
        {
            Assert.That(shown, Has.Count.EqualTo(existingDestination ? 1 : 0));
            Assert.That(copies, Is.Zero);
            Assert.That(Directory.GetFileSystemEntries(outside, "*", SearchOption.AllDirectories), Is.EquivalentTo(originalEntries));
            Assert.That(notifications.Single().Message, Is.EqualTo(MessageStrings.OperationFailed));
        });
    }

    [AvaloniaTest]
    public async Task A_folder_that_is_not_moved_is_named_in_a_warning()
    {
        (FileBrowserTabViewModel browser, string footage, string target) = await OpenAsync("move");
        using var _ = browser;
        Directory.CreateDirectory(Path.Combine(target, "footage"));
        using var notifications = new NotificationCapture();

        await browser.TransferFilesToDirectoryAsync([(footage, true)], target, move: true);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(footage, "b.txt")), Is.True);
            Assert.That(
                notifications.All.Select(n => (n.Type, n.Message)),
                Is.EqualTo(new[] { (NotificationType.Warning, string.Format(Strings.FileBrowser_FolderNotMoved, "footage")) }));
        });
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task The_folder_conflict_dialog_shows_its_three_answers(bool light)
    {
        FAContentDialog dialog = FileBrowserTabViewModel.CreateFolderConflictDialog("footage");
        var window = new Window { Width = 640, Height = 360, RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark };
        window.Show();
        Task closed = dialog.ShowAsync(window);
        try
        {
            // Past the dialog's opening animation.
            HeadlessTestHelpers.Render(ticks: 30);
            string[] buttons = [.. dialog.GetVisualDescendants().OfType<Button>()
                .Where(button => button.IsEffectivelyVisible && button.Content is string)
                .Select(button => (string)button.Content!)];
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            frame?.Save(Path.Combine(TestContext.CurrentContext.WorkDirectory, $"folder-conflict-dialog-{(light ? "light" : "dark")}.png"));

            Assert.Multiple(() =>
            {
                Assert.That(buttons, Is.EquivalentTo(new[] { Strings.FileBrowser_MergeFolder, Strings.FileBrowser_SkipFolder, Strings.Cancel }));
                Assert.That(frame, Is.Not.Null);
            });
        }
        finally
        {
            dialog.Hide();
            await closed;
            window.Close();
        }
    }

    // An editor, a source folder "footage" holding b.txt, and an empty target folder beside it.
    private static async Task<(FileBrowserTabViewModel Browser, string Footage, string Target)> OpenAsync(string name)
    {
        EditViewModel editor = (await TestShell.OpenNewSceneAsync($"filebrowser-folder-{name}", 320, 180)).Editor;
        string root = FilePathComparison.ResolveCanonicalPath(TestShell.ProjectRoot(editor));
        string footage = Directory.CreateDirectory(Path.Combine(root, "src", "footage")).FullName;
        WriteFile(footage, "b.txt", "b");
        string target = Directory.CreateDirectory(Path.Combine(root, "dst")).FullName;
        return (new FileBrowserTabViewModel(editor), footage, target);
    }

    private static List<FAContentDialog> Answer(FileBrowserTabViewModel browser, FAContentDialogResult answer)
    {
        var shown = new List<FAContentDialog>();
        browser.ConfirmAsync = dialog =>
        {
            shown.Add(dialog);
            return Task.FromResult(answer);
        };
        return shown;
    }

    private static string WriteFile(string folder, string name, string content)
    {
        string path = Path.Combine(folder, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static void IgnoreUnlessLinksCanBeCreated()
    {
        string probe = Directory.CreateDirectory(
            Path.Combine(BeutlHomeIsolation.CurrentHome!, $"link-probe-{Guid.NewGuid():N}")).FullName;
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(probe, "link"), "missing");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Assert.Ignore($"Symbolic links cannot be created here: {ex.Message}");
        }
        finally
        {
            Directory.Delete(probe, recursive: true);
        }
    }
}
