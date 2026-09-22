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
    public enum SaveStep
    {
        Project,
        Element,
        Scene,
    }

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

        bool saved = await editor.SaveAsync();
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
        bool saved = await editor.SaveAsync();
        HeadlessTestHelpers.Settle();

        Assert.That(saved, Is.True);
        Assert.That(File.Exists(element.Uri!.LocalPath), Is.True);
        Assert.That(new FileInfo(element.Uri!.LocalPath).Length, Is.GreaterThan(0));
    }

    [AvaloniaTest]
    public async Task Saving_a_migrated_scene_persists_project_version_metadata()
    {
        MigratedProject migrated = await OpenMigratedProjectAsync("migrated-save");

        Assert.That(await migrated.Editor.SaveAsync(), Is.True);
        JsonObject savedProject = ReadJson(migrated.ProjectFile);

        Assert.Multiple(() =>
        {
            Assert.That((string?)savedProject["appVersion"], Is.EqualTo(BeutlApplication.Version));
            Assert.That(
                (string?)savedProject["minAppVersion"],
                Is.EqualTo(Project.DefaultMinAppVersion));
            Assert.That(ReadJson(migrated.ElementFile)["$type"], Is.Not.Null);
            Assert.That(migrated.ReadPersistedUnrelatedSceneName(), Is.EqualTo("unrelated-original"));
        });
    }

    [AvaloniaTest]
    [TestCase(SaveStep.Project)]
    [TestCase(SaveStep.Element)]
    [TestCase(SaveStep.Scene)]
    public async Task A_failed_migrated_scene_save_restores_every_file_it_replaced(SaveStep failingStep)
    {
        MigratedProject migrated = await OpenMigratedProjectAsync($"migrated-save-failure-{failingStep}");
        SavedFiles before = migrated.ReadFiles();
        string failingFile = migrated.PathOf(failingStep);

        Exception? failure;
        using (StorageWriteTransaction.InjectFaultsForTesting((step, path) =>
               {
                   if (step == StorageWriteStep.Replace && path == failingFile)
                       throw new IOException("Injected save failure.");
               }))
        {
            failure = await CatchSaveFailureAsync(migrated.Editor);
        }

        Assert.Multiple(() =>
        {
            Assert.That(failure, Is.Not.Null);
            AssertFilesEqual(migrated.ReadFiles(), before);
            Assert.That(migrated.ReadPersistedUnrelatedSceneName(), Is.EqualTo("unrelated-original"));
            Assert.That(migrated.FindTemporaryFiles(), Is.Empty);
        });

        Assert.That(await migrated.Editor.SaveAsync(), Is.True);
        Assert.That(
            (string?)ReadJson(migrated.ProjectFile)["minAppVersion"],
            Is.EqualTo(Project.DefaultMinAppVersion));
    }

    [AvaloniaTest]
    [TestCase(SaveStep.Project)]
    [TestCase(SaveStep.Scene)]
    public async Task A_save_the_file_system_rejects_restores_every_file_it_replaced(SaveStep blockedStep)
    {
        MigratedProject migrated = await OpenMigratedProjectAsync($"migrated-save-blocked-{blockedStep}");
        SavedFiles before = migrated.ReadFiles();
        Project project = TestShell.Project.CurrentProject.Value!;
        Scene scene = migrated.Editor.Scene;
        Uri projectUri = project.Uri!;
        Uri sceneUri = scene.Uri!;
        // An existing file cannot serve as the destination's parent directory.
        if (blockedStep == SaveStep.Project)
            project.Uri = new Uri(Path.Combine(migrated.ProjectFile, "blocked.bep"));
        else
            scene.Uri = new Uri(Path.Combine(migrated.ProjectFile, "blocked.scene"));

        Exception? failure;
        try
        {
            failure = await CatchSaveFailureAsync(migrated.Editor);
        }
        finally
        {
            project.Uri = projectUri;
            scene.Uri = sceneUri;
        }

        Assert.Multiple(() =>
        {
            Assert.That(failure, Is.Not.Null);
            AssertFilesEqual(migrated.ReadFiles(), before);
        });
    }

    [AvaloniaTest]
    [TestCase(SaveStep.Element)]
    [TestCase(SaveStep.Project)]
    public async Task A_file_the_rollback_cannot_restore_keeps_the_raised_compatibility_gate(
        SaveStep unrestorableStep)
    {
        MigratedProject migrated = await OpenMigratedProjectAsync(
            $"migrated-save-unrestorable-{unrestorableStep}");
        SavedFiles before = migrated.ReadFiles();
        string unrestorableFile = migrated.PathOf(unrestorableStep);

        Exception? failure;
        using (StorageWriteTransaction.InjectFaultsForTesting((step, path) =>
               {
                   if ((step == StorageWriteStep.Replace && path == migrated.SceneFile)
                       || (step == StorageWriteStep.Restore && path == unrestorableFile))
                   {
                       throw new IOException("Injected storage failure.");
                   }
               }))
        {
            failure = await CatchSaveFailureAsync(migrated.Editor);
        }

        Assert.Multiple(() =>
        {
            Assert.That(failure, Is.Not.Null);
            // Migrated bytes may still be on disk, so older applications must keep refusing the project.
            Assert.That(
                (string?)ReadJson(migrated.ProjectFile)["minAppVersion"],
                Is.EqualTo(Project.DefaultMinAppVersion));
            Assert.That(File.ReadAllBytes(migrated.SceneFile), Is.EqualTo(before.Scene));
            if (unrestorableStep == SaveStep.Element)
                Assert.That(ReadJson(migrated.ElementFile)["$type"], Is.Not.Null);
            else
                Assert.That(File.ReadAllBytes(migrated.ElementFile), Is.EqualTo(before.Element));
            Assert.That(migrated.ReadPersistedUnrelatedSceneName(), Is.EqualTo("unrelated-original"));
        });

        Assert.That(await migrated.Editor.SaveAsync(), Is.True);
        Assert.That(ReadJson(migrated.ElementFile)["$type"], Is.Not.Null);
    }

    // Opens a project whose element predates discriminators, so saving its scene raises minAppVersion.
    // A second scene has an unsaved rename that a save of the first must not write.
    private static async Task<MigratedProject> OpenMigratedProjectAsync(string name)
    {
        await ResetProjectAsync();

        Project project = (await TestShell.Project.CreateProject(
            640, 480, 30, 44100, name, NewWorkspace(name)))!;
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
        Assert.That(await sourceEditor.SaveAsync(), Is.True);
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

        JsonObject projectJson = ReadJson(projectFile);
        projectJson["appVersion"] = "1.0.0";
        projectJson["minAppVersion"] = "1.0.0";
        projectJson.JsonSave(projectFile);
        JsonObject elementJson = ReadJson(elementFile);
        elementJson.Remove("$type");
        elementJson.JsonSave(elementFile);

        await TestShell.Project.OpenProject(projectFile);
        Project reopened = TestShell.Project.CurrentProject.Value!;
        Scene migratedScene = reopened.Items
            .OfType<Scene>()
            .Single(scene => scene.Id == sourceScene.Id);
        Scene unrelated = reopened.Items
            .OfType<Scene>()
            .Single(scene => scene.Id == unrelatedSceneId);
        unrelated.Name = "unrelated-unsaved";
        TestShell.Editor.ActivateTabItem(migratedScene);
        HeadlessTestHelpers.Settle();
        var editor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;

        return new MigratedProject(
            Path.GetFullPath(projectFile),
            Path.GetFullPath(migratedScene.Uri!.LocalPath),
            Path.GetFullPath(elementFile),
            unrelated.Uri!,
            editor);
    }

    private static async Task<Exception?> CatchSaveFailureAsync(EditViewModel editor)
    {
        try
        {
            await editor.SaveAsync();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private static JsonObject ReadJson(string path) => JsonNode.Parse(File.ReadAllText(path))!.AsObject();

    private static void AssertFilesEqual(SavedFiles actual, SavedFiles expected)
    {
        Assert.That(actual.Project, Is.EqualTo(expected.Project), "project file");
        Assert.That(actual.Scene, Is.EqualTo(expected.Scene), "scene file");
        Assert.That(actual.Element, Is.EqualTo(expected.Element), "element file");
    }

    private sealed record SavedFiles(byte[] Project, byte[] Scene, byte[] Element);

    private sealed record MigratedProject(
        string ProjectFile,
        string SceneFile,
        string ElementFile,
        Uri UnrelatedSceneUri,
        EditViewModel Editor)
    {
        public string PathOf(SaveStep step) => step switch
        {
            SaveStep.Project => ProjectFile,
            SaveStep.Element => ElementFile,
            _ => SceneFile,
        };

        public SavedFiles ReadFiles() => new(
            File.ReadAllBytes(ProjectFile),
            File.ReadAllBytes(SceneFile),
            File.ReadAllBytes(ElementFile));

        public string ReadPersistedUnrelatedSceneName()
            => CoreSerializer.RestoreFromUri<Scene>(UnrelatedSceneUri).Name;

        public string[] FindTemporaryFiles() => Directory.GetFiles(
            Path.GetDirectoryName(ProjectFile)!,
            "*.tmp",
            SearchOption.AllDirectories);
    }
}
