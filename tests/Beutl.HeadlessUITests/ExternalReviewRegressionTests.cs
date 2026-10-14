using System.Text.Json.Nodes;
using Avalonia.Headless.NUnit;
using Beutl.Editor;
using Beutl.Editor.Components.FileBrowserTab.ViewModels;
using Beutl.Editor.Services;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.ViewModels;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class ExternalReviewRegressionTests
{
    [AvaloniaTest]
    public async Task FileBrowser_ProtectsNestedFileBackedObjects()
    {
        EditViewModel editor = (await TestShell.OpenNewSceneAsync("nested-sidecar-review", 320, 180)).Editor;
        string path = Path.Combine(Path.GetDirectoryName(editor.Scene.Uri!.LocalPath)!, "brush.json");
        var brush = new Beutl.Media.SolidColorBrush(Beutl.Media.Colors.Red) { Uri = new Uri(path) };
        var shape = new Beutl.Graphics.Shapes.RectShape();
        shape.Fill.CurrentValue = brush;
        var element = new Element();
        element.Objects.Add(shape);
        editor.Scene.Children.Add(element);
        File.WriteAllText(path, "{}");
        using var browser = new FileBrowserTabViewModel(editor);
        using var item = new FileSystemItemViewModel(path, false);
        await browser.RenameItemAsync(item, "renamed-brush.json");
        Assert.That(File.Exists(path), Is.True);
        await browser.DeleteItemAsync(item);
        Assert.That(File.Exists(path), Is.True);
    }

    [AvaloniaTest]
    public async Task ReopeningCurrentProject_PreservesUnsavedSceneEdits()
    {
        EditViewModel editor = (await TestShell.OpenNewSceneAsync("reopen-unsaved-review", 320, 180)).Editor;
        Project project = TestShell.Project.CurrentProject.Value!;
        bool autoSave = EditViewModel.IsAutoSaveSuppressedForTesting;
        try
        {
            EditViewModel.IsAutoSaveSuppressedForTesting = true;
            editor.Scene.Duration = TimeSpan.FromSeconds(73);
            await TestShell.Project.OpenProject(project.Uri!.LocalPath);
            Assert.That(TestShell.Project.CurrentProject.Value!.Items.OfType<Scene>().First().Duration,
                Is.EqualTo(TimeSpan.FromSeconds(73)));
        }
        finally
        {
            EditViewModel.IsAutoSaveSuppressedForTesting = autoSave;
        }
    }

    [AvaloniaTest]
    public async Task Edits_are_auto_saved_even_when_a_legacy_config_disabled_auto_save()
    {
        EditViewModel editor = (await TestShell.OpenNewSceneAsync("legacy-auto-save-off", 320, 180)).Editor;
        // Configs written before auto save became mandatory may still carry the old opt-out.
        CoreSerializer.PopulateFromJsonObject(
            Beutl.Configuration.GlobalConfiguration.Instance.EditorConfig,
            new JsonObject { ["IsAutoSaveEnabled"] = false });

        ElementAddResult result = await TestShell.AddElementAsync(
            editor, () => new Beutl.Graphics.Shapes.RectShape(), TimeSpan.Zero, TimeSpan.FromSeconds(1));
        Assert.That(result.IsSuccess, Is.True, result.Failure?.Message);
        Element element = editor.Scene.Children.Single();
        // Let the save queued by the add land first, so only the rename's own save can write the name.
        Assert.That(WaitForSavedName(element, n => n != null), Is.Not.Null);

        element.Name = "auto-saved";
        editor.HistoryManager.Commit();

        string? saved = WaitForSavedName(element, n => n == "auto-saved");
        Assert.That(saved, Is.EqualTo("auto-saved"));
    }

    private static string? WaitForSavedName(Element element, Func<string?, bool> done)
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        string? saved;
        do
        {
            HeadlessTestHelpers.Settle();
            saved = File.Exists(element.Uri!.LocalPath)
                ? CoreSerializer.RestoreFromUri<Element>(element.Uri).Name
                : null;
        } while (!done(saved) && DateTime.UtcNow < deadline);

        return saved;
    }

    [AvaloniaTest]
    public async Task InvalidProjectOpen_KeepsTheCurrentProject()
    {
        EditViewModel editor = (await TestShell.OpenNewSceneAsync("invalid-open-review", 320, 180)).Editor;
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
        EditViewModel editor = (await TestShell.OpenNewSceneAsync("output-profile-review", 320, 180)).Editor;
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

    [AvaloniaTest]
    public async Task FileBrowser_DoesNotRenameOrDeleteAnOpenScene()
    {
        EditViewModel editor = (await TestShell.OpenNewSceneAsync("open-file-review", 320, 180)).Editor;
        string path = editor.Scene.Uri!.LocalPath;
        using var browser = new FileBrowserTabViewModel(editor);
        using var item = new FileSystemItemViewModel(path, false);
        await browser.RenameItemAsync(item, "renamed.scene");
        await browser.DeleteItemAsync(item);
        Assert.That(File.Exists(path), Is.True);
        Assert.That(File.Exists(Path.Combine(Path.GetDirectoryName(path)!, "renamed.scene")), Is.False);
    }
}
