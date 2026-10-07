using Avalonia.Headless.NUnit;
using Beutl.Editor.Components.FileBrowserTab.ViewModels;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;

namespace Beutl.HeadlessUITests;

// Only a case-sensitive file system holds Foo and foo side by side; elsewhere these tests are ignored.
[TestFixture]
public sealed class FileBrowserMoveCaseTests
{
    [AvaloniaTest]
    public async Task A_file_moves_into_a_sibling_folder_that_differs_only_in_case()
    {
        EditViewModel editor = await CreateEditor("filebrowser-move-case-file");
        (string upper, string lower) = CaseVariantFolders(ProjectRoot(editor));
        string source = Path.Combine(upper, "clip.txt");
        File.WriteAllText(source, "clip");
        using var browser = new FileBrowserTabViewModel(editor);

        browser.MoveFilesToDirectory([(source, false)], lower);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(lower, "clip.txt")), Is.True, "The move was skipped as a same-folder drop.");
            Assert.That(File.Exists(source), Is.False);
        });
    }

    [AvaloniaTest]
    public async Task A_folder_moves_under_a_sibling_folder_that_differs_only_in_case()
    {
        EditViewModel editor = await CreateEditor("filebrowser-move-case-folder");
        (string upper, string lower) = CaseVariantFolders(ProjectRoot(editor));
        string target = Directory.CreateDirectory(Path.Combine(lower, "sub")).FullName;
        using var browser = new FileBrowserTabViewModel(editor);

        browser.MoveFilesToDirectory([(upper, true)], target);

        Assert.Multiple(() =>
        {
            Assert.That(Directory.Exists(Path.Combine(target, "Foo")), Is.True, "The move was refused as a move into itself.");
            Assert.That(Directory.Exists(upper), Is.False);
        });
    }

    private static (string Upper, string Lower) CaseVariantFolders(string root)
    {
        string upper = Directory.CreateDirectory(Path.Combine(root, "Foo")).FullName;
        if (Directory.Exists(Path.Combine(root, "foo")))
            Assert.Ignore("The file system ignores case, so Foo and foo are the same folder.");

        return (upper, Directory.CreateDirectory(Path.Combine(root, "foo")).FullName);
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
