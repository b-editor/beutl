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

    [AvaloniaTest]
    public async Task UnavailableReadOnlyOutputProfile_SurvivesARewrite()
    {
        EditViewModel editor = await CreateEditor("output-profile-review");
        string path = Path.Combine(Path.GetDirectoryName(editor.Scene.Uri!.LocalPath)!, EditorConstants.BeutlFolder, "output-profile.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var original = new JsonArray(new JsonObject
        {
            ["Extension"] = "[Missing.Plugin]Missing:Output",
            ["File"] = editor.Scene.Uri.LocalPath,
            ["Context"] = new JsonObject { ["Name"] = "retained", ["PluginSetting"] = 42 },
        });
        File.WriteAllText(path, original.ToJsonString());
        using var service = new OutputService(editor);
        File.SetAttributes(path, FileAttributes.ReadOnly);
        try { service.RestoreItems(); }
        finally { File.SetAttributes(path, FileAttributes.Normal); }
        File.Delete(path);
        service.SaveItems();
        Assert.That(File.Exists(path), Is.True, "Read-only input must not disable later saves.");
        Assert.That(JsonNode.DeepEquals(original, JsonNode.Parse(File.ReadAllText(path))), Is.True);
    }

}
