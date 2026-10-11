using Avalonia.Headless.NUnit;
using Beutl.Editor.Components.FileBrowserTab.ViewModels;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;

namespace Beutl.HeadlessUITests;

// The file system is probed in a scratch folder before any editor opens, so an ignored test leaves nothing behind.
[TestFixture]
public sealed class FileBrowserMoveCaseTests
{
    [AvaloniaTest]
    public async Task A_file_moves_into_a_sibling_folder_that_differs_only_in_case()
    {
        IgnoreUnlessCaseSensitive();
        EditViewModel editor = await CreateEditor("filebrowser-move-case-file");
        (string upper, string lower) = CaseVariantFolders(ProjectRoot(editor));
        string source = Path.Combine(upper, "clip.txt");
        File.WriteAllText(source, "clip");
        using var browser = new FileBrowserTabViewModel(editor);

        await browser.MoveFilesToDirectoryAsync([(source, false)], lower);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(lower, "clip.txt")), Is.True, "The move was skipped as a same-folder drop.");
            Assert.That(File.Exists(source), Is.False);
        });
    }

    [AvaloniaTest]
    public async Task A_folder_moves_under_a_sibling_folder_that_differs_only_in_case()
    {
        IgnoreUnlessCaseSensitive();
        EditViewModel editor = await CreateEditor("filebrowser-move-case-folder");
        (string upper, string lower) = CaseVariantFolders(ProjectRoot(editor));
        string target = Directory.CreateDirectory(Path.Combine(lower, "sub")).FullName;
        using var browser = new FileBrowserTabViewModel(editor);

        await browser.MoveFilesToDirectoryAsync([(upper, true)], target);

        Assert.Multiple(() =>
        {
            Assert.That(Directory.Exists(Path.Combine(target, "Foo")), Is.True, "The move was refused as a move into itself.");
            Assert.That(Directory.Exists(upper), Is.False);
        });
    }

    [AvaloniaTest]
    public async Task A_folder_link_moves_into_the_folder_it_points_to()
    {
        IgnoreUnlessLinksCanBeCreated();
        EditViewModel editor = await CreateEditor("filebrowser-move-link");
        string root = ProjectRoot(editor);
        string real = Directory.CreateDirectory(Path.Combine(root, "real")).FullName;
        string target = Directory.CreateDirectory(Path.Combine(real, "sub")).FullName;
        // A relative target leaves the moved link dangling rather than looping back into real.
        string link = Directory.CreateSymbolicLink(Path.Combine(root, "link"), "real").FullName;
        using var browser = new FileBrowserTabViewModel(editor);

        await browser.MoveFilesToDirectoryAsync([(link, true)], target);

        // Directory.Move relocates the link itself, so nothing moves into itself.
        Assert.Multiple(() =>
        {
            Assert.That(new DirectoryInfo(Path.Combine(target, "link")).LinkTarget, Is.Not.Null, "The move was refused as a move into itself.");
            Assert.That(Path.Exists(link), Is.False);
        });
    }

    private static void IgnoreUnlessCaseSensitive()
    {
        string probe = CreateScratchFolder("case-probe");
        Directory.CreateDirectory(Path.Combine(probe, "Foo"));
        bool ignoresCase = Directory.Exists(Path.Combine(probe, "foo"));
        Directory.Delete(probe, recursive: true);

        if (ignoresCase)
            Assert.Ignore("The file system ignores case, so Foo and foo are the same folder.");
    }

    private static void IgnoreUnlessLinksCanBeCreated()
    {
        string probe = CreateScratchFolder("link-probe");
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(probe, "link"), "missing");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Assert.Ignore($"Symbolic links cannot be created here: {ex.Message}");
        }
        finally
        {
            Directory.Delete(probe, recursive: true);
        }
    }

    private static string CreateScratchFolder(string name)
        => Directory.CreateDirectory(Path.Combine(BeutlHomeIsolation.CurrentHome!, $"{name}-{Guid.NewGuid():N}")).FullName;

    private static (string Upper, string Lower) CaseVariantFolders(string root)
    {
        return (
            Directory.CreateDirectory(Path.Combine(root, "Foo")).FullName,
            Directory.CreateDirectory(Path.Combine(root, "foo")).FullName);
    }

    private static async Task<EditViewModel> CreateEditor(string name)
    {
        await TestReset.ResetShellAsync();
        string directory = Path.Combine(BeutlHomeIsolation.CurrentHome!, name);
        Directory.CreateDirectory(directory);
        Project project = (await TestShell.Project.CreateProject(320, 180, 30, 44100, name, directory))!;
        TestShell.Editor.ActivateTabItem(project.Items.OfType<Scene>().First());
        HeadlessTestHelpers.Settle();
        return (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
    }

    private static string ProjectRoot(EditViewModel editor)
        => Path.GetDirectoryName(editor.Scene.Uri!.LocalPath)!;
}
