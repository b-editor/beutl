using System.Text.Json.Nodes;
using Avalonia.Headless.NUnit;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Graphics.Shapes;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.ViewModels;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class SaveRoundTripTests
{
    private static Task ResetProjectAsync() => TestReset.ResetShellAsync();

    private static string NewWorkspace(string name)
    {
        string location = Path.Combine(BeutlHomeIsolation.CurrentHome!, name);
        Directory.CreateDirectory(location);
        return location;
    }

    [AvaloniaTest]
    public async Task Mutation_made_through_the_editor_survives_save_and_reopen()
    {
        await ResetProjectAsync();

        Project project = (await TestShell.Project.CreateProject(
            640, 480, 30, 44100, "saveroundtrip", NewWorkspace("saveroundtrip")))!;
        HeadlessTestHelpers.Settle();
        string projectFile = project.Uri!.LocalPath;
        Scene scene = project.Items.OfType<Scene>().First();

        TestShell.Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        var editor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;

        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        await adder.AddAsync([new ElementDescription(
            Start: TimeSpan.FromSeconds(1),
            Length: TimeSpan.FromSeconds(3),
            Layer: 2,
            Source: new ElementSource.EngineObject(
                () => new RectShape { Width = { CurrentValue = 321 } }),
            Name: "RoundTripRect")],
            CancellationToken.None);
        HeadlessTestHelpers.Settle();

        Element element = editor.Scene.Children.Single();
        Guid elementId = element.Id;
        element.ZIndex = 4;
        editor.HistoryManager.Commit("EditZIndex");
        HeadlessTestHelpers.Settle();

        bool saved = await editor.Commands!.OnSave();
        HeadlessTestHelpers.Settle();
        Assert.That(saved, Is.True);

        await ResetProjectAsync();

        await TestShell.Project.OpenProject(projectFile);
        HeadlessTestHelpers.Settle();

        Scene reopenedScene = BeutlApplication.Current.Project!.Items.OfType<Scene>().Single();
        Element reopenedElement = reopenedScene.Children.Single(e => e.Id == elementId);
        Assert.That(reopenedElement.Name, Is.EqualTo("RoundTripRect"));
        Assert.That(reopenedElement.ZIndex, Is.EqualTo(4));
        Assert.That(reopenedElement.Start, Is.EqualTo(TimeSpan.FromSeconds(1)));

        RectShape reopenedRect = reopenedElement.Objects.OfType<RectShape>().Single();
        Assert.That(reopenedRect.Width.CurrentValue, Is.EqualTo(321));
    }

    [AvaloniaTest]
    public async Task Saving_writes_the_element_file_to_disk()
    {
        await ResetProjectAsync();

        Project project = (await TestShell.Project.CreateProject(
            640, 480, 30, 44100, "savewrites", NewWorkspace("savewrites")))!;
        HeadlessTestHelpers.Settle();
        Scene scene = project.Items.OfType<Scene>().First();

        TestShell.Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        var editor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;

        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        await adder.AddAsync([new ElementDescription(
            Start: TimeSpan.Zero,
            Length: TimeSpan.FromSeconds(2),
            Layer: 0,
            Source: new ElementSource.EngineObject(() => new RectShape()))],
            CancellationToken.None);
        HeadlessTestHelpers.Settle();

        Element element = editor.Scene.Children.Single();
        bool saved = await editor.Commands!.OnSave();
        HeadlessTestHelpers.Settle();

        Assert.That(saved, Is.True);
        Assert.That(File.Exists(element.Uri!.LocalPath), Is.True);
        Assert.That(new FileInfo(element.Uri!.LocalPath).Length, Is.GreaterThan(0));
    }

    [AvaloniaTest]
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public async Task Saving_a_migrated_scene_persists_project_version_metadata(int failureStage)
    {
        await ResetProjectAsync();

        Project project = (await TestShell.Project.CreateProject(
            640, 480, 30, 44100, "migrated-save", NewWorkspace("migrated-save")))!;
        string projectFile = project.Uri!.LocalPath;
        Scene sourceScene = project.Items.OfType<Scene>().Single();
        TestShell.Editor.ActivateTabItem(sourceScene);
        HeadlessTestHelpers.Settle();
        var sourceEditor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
        var adder = (IElementAdder)sourceEditor.GetService(typeof(IElementAdder))!;
        await adder.AddAsync([new ElementDescription(
            Start: TimeSpan.Zero,
            Length: TimeSpan.FromSeconds(1),
            Layer: 0,
            Source: new ElementSource.EngineObject(() => new RectShape()))],
            CancellationToken.None);
        HeadlessTestHelpers.Settle();
        Assert.That(await sourceEditor.Commands!.OnSave(), Is.True);
        string elementFile = sourceScene.Children.Single().Uri!.LocalPath;
        var unrelatedScene = new Scene
        {
            Name = "unrelated-original",
            Uri = new Uri(Path.Combine(
                Path.GetDirectoryName(projectFile)!,
                "unrelated.scene")),
        };
        Guid unrelatedSceneId = unrelatedScene.Id;
        project.Items.Add(unrelatedScene);
        CoreSerializer.StoreToUri(unrelatedScene, unrelatedScene.Uri);
        CoreSerializer.StoreToUri(
            project,
            project.Uri,
            CoreSerializationMode.Write);
        await ResetProjectAsync();

        JsonObject projectJson = JsonNode.Parse(File.ReadAllText(projectFile))!.AsObject();
        projectJson["appVersion"] = "1.0.0";
        projectJson["minAppVersion"] = "1.0.0";
        projectJson.JsonSave(projectFile);
        JsonObject elementJson = JsonNode.Parse(File.ReadAllText(elementFile))!.AsObject();
        elementJson.Remove("$type");
        elementJson.JsonSave(elementFile);

        await TestShell.Project.OpenProject(projectFile);
        Scene migratedScene = TestShell.Project.CurrentProject.Value!.Items
            .OfType<Scene>()
            .Single(scene => scene.Id == sourceScene.Id);
        Scene unrelated = TestShell.Project.CurrentProject.Value.Items
            .OfType<Scene>()
            .Single(scene => scene.Id == unrelatedSceneId);
        unrelated.Name = "unrelated-unsaved";
        TestShell.Editor.ActivateTabItem(migratedScene);
        HeadlessTestHelpers.Settle();
        var editor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;

        if (failureStage != 0)
        {
            Project currentProject = TestShell.Project.CurrentProject.Value!;
            Uri originalUri = currentProject.Uri!;
            Uri originalSceneUri = migratedScene.Uri!;
            byte[] projectBefore = File.ReadAllBytes(projectFile);
            byte[] sceneBefore = File.ReadAllBytes(migratedScene.Uri!.LocalPath);
            byte[] elementBefore = File.ReadAllBytes(elementFile);
            // An existing file cannot serve as the destination's parent directory.
            if (failureStage == 1)
                currentProject.Uri = new Uri(Path.Combine(projectFile, "blocked.bep"));
            else
                migratedScene.Uri = new Uri(Path.Combine(projectFile, "blocked.scene"));
            Exception? failure = null;
            try
            {
                await editor.Commands!.OnSave();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                currentProject.Uri = originalUri;
                migratedScene.Uri = originalSceneUri;
            }

            Assert.Multiple(() =>
            {
                Assert.That(failure, Is.Not.Null);
                Assert.That(File.ReadAllBytes(migratedScene.Uri.LocalPath), Is.EqualTo(sceneBefore));
                if (failureStage == 1)
                {
                    Assert.That(File.ReadAllBytes(projectFile), Is.EqualTo(projectBefore));
                    Assert.That(File.ReadAllBytes(elementFile), Is.EqualTo(elementBefore));
                }
                else
                {
                    // The element write succeeded before the scene failed. Lowering the
                    // project gate here would expose migrated bytes to older applications.
                    JsonObject persistedProject = JsonNode.Parse(File.ReadAllText(projectFile))!.AsObject();
                    Assert.That((string?)persistedProject["minAppVersion"],
                        Is.EqualTo(Project.DefaultMinAppVersion));
                    Assert.That(JsonNode.Parse(File.ReadAllText(elementFile))!["$type"], Is.Not.Null);
                }
            });
            return;
        }

        Assert.That(await editor.Commands!.OnSave(), Is.True);
        JsonObject savedProject = JsonNode.Parse(File.ReadAllText(projectFile))!.AsObject();
        Scene persistedUnrelated = CoreSerializer.RestoreFromUri<Scene>(unrelated.Uri!);

        Assert.Multiple(() =>
        {
            Assert.That((string?)savedProject["appVersion"], Is.EqualTo(BeutlApplication.Version));
            Assert.That(
                (string?)savedProject["minAppVersion"],
                Is.EqualTo(Project.DefaultMinAppVersion));
            Assert.That(persistedUnrelated.Name, Is.EqualTo("unrelated-original"));
        });
    }
}
