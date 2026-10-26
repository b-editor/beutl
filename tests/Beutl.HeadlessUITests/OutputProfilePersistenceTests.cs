using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Beutl.Editor;
using Beutl.Editor.Components.OutputTab;
using Beutl.Editor.Components.OutputTab.ViewModels;
using Beutl.Editor.Components.OutputTab.Views;
using Beutl.Extensibility;
using Beutl.Media;
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
    public async Task ObjectId_SelectsTheOwningSceneAndPreservesOtherProfiles()
    {
        EditViewModel editor = (await TestShell.OpenNewSceneAsync("output-identity", 320, 180)).Editor;
        string path = ProfilePath(editor);
        JsonObject profile;
        using (var template = new OutputTabViewModel(editor))
        {
            template.Items.Single().Context.Name.Value = "Other scene";
            profile = OutputProfileItem.ToJson(template.Items[0]).AsObject();
        }
        Guid otherId = Guid.NewGuid();
        profile["ObjectId"] = otherId;
        // Even a valid legacy path must not override an explicit object identity.
        profile["File"] = editor.Scene.Uri!.LocalPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        new JsonArray(profile).JsonSave(path);

        using var service = new OutputService(editor);
        service.RestoreItems();
        Assert.That(service.Items, Is.Empty);
        service.SaveItems();
        Assert.That(JsonNode.DeepEquals(profile, JsonNode.Parse(File.ReadAllText(path))![0]), Is.True);

        editor.Scene.Id.Value = otherId;
        service.RestoreItems();
        Assert.That(service.Items.Single().Context.Name.Value, Is.EqualTo("Other scene"));
    }

    private static string ProfilePath(EditViewModel editor)
        => Path.Combine(TestShell.ProjectRoot(editor), EditorConstants.BeutlFolder, "output-profile.json");
}
