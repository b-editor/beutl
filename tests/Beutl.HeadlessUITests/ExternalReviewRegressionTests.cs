using System.Text.Json.Nodes;
using Avalonia.Headless.NUnit;
using Beutl.Editor;
using Beutl.Editor.Components.FileBrowserTab.ViewModels;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.ViewModels;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class ExternalReviewRegressionTests
{
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

    [AvaloniaTest]
    public async Task InvalidProjectOpen_KeepsTheCurrentProject()
    {
        EditViewModel editor = await CreateEditor("invalid-open-review");
        Project current = TestShell.Project.CurrentProject.Value!;
        string invalid = Path.Combine(Path.GetDirectoryName(editor.Scene.Uri!.LocalPath)!, "invalid.bep");
        File.WriteAllText(invalid, "{ invalid json");
        await TestShell.Project.OpenProject(invalid);
        Assert.That(TestShell.Project.CurrentProject.Value, Is.SameAs(current));
        Assert.That(TestShell.Editor.TabItems.Any(tab => ReferenceEquals(tab.Context.Value, editor)), Is.True);
    }

}
