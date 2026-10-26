using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Beutl.Editor;
using Beutl.Editor.Components.OutputTab;
using Beutl.Editor.Components.OutputTab.ViewModels;
using Beutl.Editor.Components.OutputTab.Views;
using Beutl.Extensibility;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Services.PrimitiveImpls;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Tools;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class OutputProfilePersistenceTests
{
    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task MovingAndRenamingProject_RestoresProfilesAndMigratesLegacyPaths(bool legacy)
    {
        (Project project, var scene, EditViewModel editor) =
            await TestShell.OpenNewSceneAsync("output-move", 320, 180);
        const int packageId = 1938202;
        var encoder = new TestEncoder();
        TestShell.Extensions.AddExtensions(packageId, [encoder]);
        string projectPath = project.Uri!.LocalPath;
        string scenePath = scene.Uri!.LocalPath;
        string profilePath = ProfilePath(editor);
        string destination = Path.Combine(TestShell.NewWorkspace("output-destination"), "video.mp4");
        Guid sceneId = scene.Id.Value;
        try
        {
            using (var tab = new OutputTabViewModel(editor))
            {
                var output = (OutputViewModel)tab.Items.Single().Context;
                output.Name.Value = "Delivery";
                output.DestinationFile.Value = destination;
                output.SelectedEncoder.Value = encoder;
                output.SupersampleFactor.Value = 2;
                EncodingController controller = output.Controller.Value!;
                controller.VideoSettings.DestinationSize.Value = new PixelSize(1280, 720);
                controller.VideoSettings.FrameRate.Value = new Rational(24000, 1001);
                controller.VideoSettings.Bitrate.Value = 12_000_000;
                controller.AudioSettings.SampleRate.Value = 48000;
                tab.AddItem(SceneOutputExtension.Instance);
                tab.Items[1].Context.Name.Value = "Preview";
                tab.Save();
            }

            // Closing the actual editor can save its tool tabs, so place the saved profiles back
            // after closing, before moving the whole project directory as a user would.
            JsonArray profiles = JsonNode.Parse(File.ReadAllText(profilePath))!.AsArray();
            await TestReset.ResetShellAsync();
            if (legacy)
            {
                foreach (JsonNode? profile in profiles)
                {
                    profile!.AsObject().Remove("ObjectId");
                    profile["File"] = scenePath;
                }
            }
            profiles.JsonSave(profilePath);

            string source = Path.GetDirectoryName(projectPath)!;
            string moved = Path.Combine(TestShell.NewWorkspace($"output-moved-{Guid.NewGuid():N}"), "renamed-project");
            Directory.Move(source, moved);
            string movedProjectPath = Path.Combine(moved, "renamed.bep");
            File.Move(Path.Combine(moved, Path.GetFileName(projectPath)), movedProjectPath);
            Assert.That(File.Exists(scenePath), Is.False);

            EditViewModel reopened = await TestShell.ReopenAsync(movedProjectPath);
            using var restored = new OutputTabViewModel(reopened);
            Assert.That(reopened.Scene.Id.Value, Is.EqualTo(sceneId));
            Assert.That(restored.Items.Select(item => item.Context.Name.Value),
                Is.EqualTo(new[] { "Delivery", "Preview" }));
            Assert.That(restored.SelectedItem.Value, Is.SameAs(restored.Items[0]));
            var restoredOutput = (OutputViewModel)restored.Items[0].Context;
            Assert.That(restoredOutput.Object, Is.SameAs(reopened.Scene));
            Assert.That(restoredOutput.Controller.Value, Is.Not.Null);
            EncodingController restoredController = restoredOutput.Controller.Value!;
            Assert.Multiple(() =>
            {
                Assert.That(restoredOutput.DestinationFile.Value, Is.EqualTo(destination));
                Assert.That(restoredOutput.SelectedEncoder.Value, Is.SameAs(encoder));
                Assert.That(restoredOutput.SupersampleFactor.Value, Is.EqualTo(2));
                Assert.That(restoredController.VideoSettings.DestinationSize.Value, Is.EqualTo(new PixelSize(1280, 720)));
                Assert.That(restoredController.VideoSettings.FrameRate.Value, Is.EqualTo(new Rational(24000, 1001)));
                Assert.That(restoredController.VideoSettings.Bitrate.Value, Is.EqualTo(12_000_000));
                Assert.That(restoredController.AudioSettings.SampleRate.Value, Is.EqualTo(48000));
            });

            restored.Save();
            JsonArray saved = JsonNode.Parse(File.ReadAllText(ProfilePath(reopened)))!.AsArray();
            Assert.That(saved, Has.Count.EqualTo(2));
            foreach (JsonNode? profile in saved)
            {
                Assert.That(profile!["ObjectId"]!.GetValue<Guid>(), Is.EqualTo(sceneId));
                Assert.That(profile.AsObject().ContainsKey("File"), Is.False);
            }

            if (HeadlessCapture.IsRequested)
            {
                var window = new Window
                {
                    Content = new OutputTabView { DataContext = restored },
                    Width = 720,
                    Height = 800
                };
                try
                {
                    window.Show();
                    HeadlessTestHelpers.Settle();
                    HeadlessCapture.SaveIfRequested(window, nameof(OutputProfilePersistenceTests), $"moved-legacy-{legacy}");
                }
                finally
                {
                    window.Close();
                }
            }
        }
        finally
        {
            await TestReset.ResetShellAsync();
            TestShell.Extensions.RemoveExtensions(packageId);
        }
    }

    [AvaloniaTest]
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task SharedDirectory_RestoresOnlyTheOwningSceneAndPreservesOtherProfiles(bool legacy, bool move)
    {
        (Project project, Scene scene, EditViewModel editor) =
            await TestShell.OpenNewSceneAsync("output-identity", 320, 180);
        string projectPath = project.Uri!.LocalPath;
        string scenePath = scene.Uri!.LocalPath;
        Guid sceneId = scene.Id.Value;
        string otherPath = Path.Combine(TestShell.ProjectRoot(editor), "other.scene");
        CoreSerializer.StoreToUri(new Scene(320, 180, "Other"), UriHelper.CreateFromPath(otherPath));
        TestShell.MainViewModel.MenuBar.OpenFileCore(otherPath);
        Assert.That(project.Items, Has.Count.EqualTo(2), "Opening a sibling scene is supported by the editor.");

        string path = ProfilePath(editor);
        JsonObject profile;
        using (var template = new OutputTabViewModel(editor))
        {
            template.Items.Single().Context.Name.Value = "Original scene";
            profile = OutputProfileItem.ToJson(template.Items[0]).AsObject();
        }
        if (legacy)
            profile.Remove("ObjectId");
        // An explicit ID must win even if a legacy path names the other scene.
        profile["File"] = legacy ? scenePath : otherPath;
        await TestReset.ResetShellAsync();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        new JsonArray(profile).JsonSave(path);

        if (move)
        {
            string moved = Path.Combine(TestShell.NewWorkspace($"output-shared-{Guid.NewGuid():N}"), "moved");
            Directory.Move(Path.GetDirectoryName(projectPath)!, moved);
            projectPath = Path.Combine(moved, Path.GetFileName(projectPath));
        }
        Assert.That(File.Exists(scenePath), Is.EqualTo(!move));

        await TestShell.Project.OpenProject(projectPath);
        Project reopened = TestShell.Project.CurrentProject.Value!;
        TestShell.Editor.ActivateTabItem(reopened.Items.Single(item => item.Id.Value != sceneId));
        var otherEditor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
        using (var service = new OutputService(otherEditor))
        {
            service.RestoreItems();
            Assert.That(service.Items, Is.Empty);
            service.SaveItems();
            Assert.That(JsonNode.DeepEquals(profile, JsonNode.Parse(File.ReadAllText(ProfilePath(otherEditor)))![0]), Is.True);
        }

        TestShell.Editor.ActivateTabItem(reopened.Items.Single(item => item.Id.Value == sceneId));
        var ownerEditor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
        using var ownerService = new OutputService(ownerEditor);
        ownerService.RestoreItems();
        Assert.That(ownerService.Items.Single().Context.Name.Value, Is.EqualTo("Original scene"));
        ownerService.SaveItems();
        JsonNode saved = JsonNode.Parse(File.ReadAllText(ProfilePath(ownerEditor)))![0]!;
        Assert.That(saved["ObjectId"]!.GetValue<Guid>(), Is.EqualTo(sceneId));
        Assert.That(saved.AsObject().ContainsKey("File"), Is.False);
    }

    private static string ProfilePath(EditViewModel editor)
        => Path.Combine(TestShell.ProjectRoot(editor), EditorConstants.BeutlFolder, "output-profile.json");
}
