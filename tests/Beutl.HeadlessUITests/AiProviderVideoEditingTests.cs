using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Beutl.Api;
using Beutl.Api.Services;
using Beutl.Services.AI;
using Beutl.Testing.Headless;
using Beutl.ViewModels.Dialogs;
using Beutl.ViewModels.Tools;
using Beutl.Views.Tools;
using static Beutl.HeadlessUITests.AiTestSupport;

namespace Beutl.HeadlessUITests;

public sealed partial class AiDialogWorkflowTests
{
    [AvaloniaTest]
    [TestCase(AiSourceVideoMode.Edit)]
    [TestCase(AiSourceVideoMode.Extend)]
    [TestCase(AiSourceVideoMode.Motion)]
    public async Task ProviderEditing_PreservesSourceModeOptionsAndRequestKeyAcrossRestart(AiSourceVideoMode mode)
    {
        await TestReset.ResetShellAsync();
        const string originalPrompt = "  change  the sky\nkeep\tmy subject  ";
        var sent = new List<(string Key, string Body)>();
        string route = "/api/v3/ai/videos/" + mode.ToString().ToLowerInvariant();
        await using var api = new ApiScope(new StubHandler(async (request, token) =>
        {
            if (request.RequestUri?.AbsolutePath == route)
                sent.Add((request.Headers.GetValues("Idempotency-Key").Single(), await request.Content!.ReadAsStringAsync(token)));
            return ProviderVideoResponse(request);
        }));
        BeutlApiApplication clients = api.Clients;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        using var store = new FileAiRequestRecoveryStore(Path.Combine(BeutlHomeIsolation.CurrentHome!, "restart-" + Guid.NewGuid().ToString("N")), () => now);
        using var context = new AiRequestRecoveryContext(store, () => new AiAuthenticatedRequestIdentity("test-user", null));
        string source = Path.Combine(BeutlHomeIsolation.CurrentHome!, "source.webm");
        string character = Path.Combine(BeutlHomeIsolation.CurrentHome!, "character.png");
        await File.WriteAllBytesAsync(source, [1, 2, 3]);
        await File.WriteAllBytesAsync(character, [1, 2, 3]);
        await using (var vm = CreateVideoGenerationDialog(clients, context: context, sourceMode: mode))
        {
            vm.InputPicker = (role, _) => Task.FromResult<IReadOnlyList<string>>([role == "source" ? source : character]);
            vm.VideoDurationReader = _ => TimeSpan.FromSeconds(7);
            await vm.SelectSourceVideo.ExecuteAsync();
            if (mode == AiSourceVideoMode.Motion)
            {
                await vm.SelectCharacterImage.ExecuteAsync();
                vm.Orientation.Value = vm.Orientations[1];
                vm.Quality.Value = vm.Qualities[1];
            }
            vm.Prompt.Value = originalPrompt;
            await WaitUntilAsync(() => vm.CanGenerate.Value);
            if (mode == AiSourceVideoMode.Edit)
            {
                vm.Prompt.Value = new string('a', 81);
                Assert.That(vm.CanGenerate.Value, Is.False);
                vm.Prompt.Value = originalPrompt;
            }
            await vm.Generate.ExecuteAsync();
            Assert.That(sent, Has.Count.EqualTo(1), vm.Error.Value);
            Assert.That(sent[0].Body, Does.Contain("source.webm"));
        }
        now = now.AddMinutes(16); // Let the previous process ownership fence expire.
        await using var restored = CreateVideoGenerationDialog(clients, context: context, sourceMode: mode);
        await WaitUntilAsync(() => restored.CanGenerate.Value);
        Assert.That(restored.SourceDuration.Value, Is.EqualTo(7));
        Assert.That(restored.Prompt.Value, Is.EqualTo(originalPrompt));
        Assert.That(restored.SourceVideoPath.Value, Is.EqualTo(source));
        if (mode == AiSourceVideoMode.Motion)
        {
            Assert.That(restored.CharacterImagePath.Value, Is.EqualTo(character));
            Assert.That(restored.Orientation.Value.Value, Is.EqualTo("image"));
            Assert.That(restored.Quality.Value.Value, Is.EqualTo("pro"));
        }
        await restored.Generate.ExecuteAsync();
        Assert.That(sent, Has.Count.EqualTo(2), restored.Error.Value);
        Assert.That(sent[1].Key, Is.EqualTo(sent[0].Key));
    }

    [AvaloniaTest]
    public async Task ProviderEditing_MotionOffersAndSendsTheQualitiesTheCatalogLists()
    {
        await TestReset.ResetShellAsync();
        string? sent = null;
        string capabilities = ProviderVideoCapabilities.Replace(
            "\"video.motion\":{\"models\":",
            "\"video.motion\":{\"qualities\":[\"standard\",\"ultra\"],\"models\":");
        await using var api = new ApiScope(new StubHandler(async (request, token) =>
        {
            switch (request.RequestUri?.AbsolutePath)
            {
                case "/api/v3/ai/capabilities":
                    return JsonResponse(HttpStatusCode.OK, capabilities);
                case "/api/v3/ai/videos/motion":
                    sent = await request.Content!.ReadAsStringAsync(token);
                    break;
            }
            return ProviderVideoResponse(request);
        }));
        using var context = CreateIdentityContext(() => "test-user");
        string source = Path.Combine(BeutlHomeIsolation.CurrentHome!, "motion-source.webm");
        string character = Path.Combine(BeutlHomeIsolation.CurrentHome!, "motion-character.png");
        await File.WriteAllBytesAsync(source, [1, 2, 3]);
        await File.WriteAllBytesAsync(character, [1, 2, 3]);
        await using var vm = CreateVideoGenerationDialog(api.Clients, context: context, sourceMode: AiSourceVideoMode.Motion);
        vm.InputPicker = (role, _) => Task.FromResult<IReadOnlyList<string>>([role == "source" ? source : character]);
        vm.VideoDurationReader = _ => TimeSpan.FromSeconds(7);
        await WaitUntilAsync(() => vm.Qualities.Count == 2 && vm.Qualities[1].Value == "ultra");

        await vm.SelectSourceVideo.ExecuteAsync();
        await vm.SelectCharacterImage.ExecuteAsync();
        vm.Quality.Value = vm.Qualities[1];
        vm.Prompt.Value = "dance";
        await WaitUntilAsync(() => vm.CanGenerate.Value);
        await vm.Generate.ExecuteAsync();

        Assert.That(sent, Does.Contain("ultra"), vm.Error.Value);
    }

    [AvaloniaTest]
    public async Task ProviderEditing_LocalSourceKeepsPendingModesIndependent()
    {
        await TestReset.ResetShellAsync();
        var sent = new List<(string Key, string Body)>();
        await using var api = new ApiScope(new StubHandler(async (request, token) =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/v3/ai/videos/edit")
                sent.Add((request.Headers.GetValues("Idempotency-Key").Single(), await request.Content!.ReadAsStringAsync(token)));
            return ProviderVideoResponse(request);
        }));
        BeutlApiApplication clients = api.Clients;
        using var context = CreateIdentityContext(() => "test-user");
        await using var vm = new AiVideoEditingViewModel(mode => CreateVideoGenerationDialog(clients, context: context, sourceMode: mode));
        var edit = vm.ActiveContent.Value!;
        string path = Path.Combine(BeutlHomeIsolation.CurrentHome!, "edit-source.mp4");
        await File.WriteAllBytesAsync(path, [1, 2, 3]);
        edit.InputPicker = (_, _) => Task.FromResult<IReadOnlyList<string>>([path]);
        edit.VideoDurationReader = _ => TimeSpan.FromSeconds(7);
        await edit.SelectSourceVideo.ExecuteAsync();
        edit.Prompt.Value = "original";
        await WaitUntilAsync(() => edit.CanGenerate.Value);
        await edit.Generate.ExecuteAsync();
        Assert.That(sent[0].Body, Does.Contain("edit-source.mp4").And.Not.Contain("sourceJobId"));
        vm.SelectedTask.Value = vm.Tasks.Single(task => task.Mode == AiSourceVideoMode.Extend);
        var extend = vm.ActiveContent.Value!;
        Assert.That(extend.SourceVideoPath.Value, Is.EqualTo(path));
        Assert.That(extend.Prompt.Value, Is.EqualTo("original"));
        extend.Prompt.Value = "new intent";
        vm.SelectedTask.Value = vm.Tasks[0];
        Assert.That(edit.Prompt.Value, Is.EqualTo("original"));
        await edit.Generate.ExecuteAsync();
        Assert.That(sent[1].Key, Is.EqualTo(sent[0].Key));
    }

    [AvaloniaTest, SetUICulture("ja-JP")]
    [TestCase(320)]
    [TestCase(640)]
    public async Task ProviderEditing_OneSectionRestoresModeAndUsesADistinctIcon(int width)
    {
        (_, _, var editor) = await TestShell.OpenNewSceneAsync("provider-video-ui");
        await using var workspace = TestShell.MainViewModel.CreateAiWorkspaceViewModel(editor);
        var generation = workspace.Sections.Single(section => section.Id == AiWorkspaceSection.VideoGeneration);
        var editing = workspace.Sections.Single(section => section.Id == AiWorkspaceSection.VideoEditing);
        Assert.That(editing.Icon, Is.Not.EqualTo(generation.Icon));
        Assert.That(workspace.Sections.Count(section => section.Id == AiWorkspaceSection.VideoEditing), Is.EqualTo(1));
        workspace.ReadFromJson(new JsonObject { ["section"] = "VideoEditing", ["videoMode"] = "Motion" });
        var restored = (AiVideoEditingViewModel)workspace.ActiveContent.Value!;
        Assert.That(restored.SelectedTask.Value.Mode, Is.EqualTo(AiSourceVideoMode.Motion));
        var layout = new JsonObject(); workspace.WriteToJson(layout);
        Assert.That(layout.Count, Is.EqualTo(2));
        Assert.That(layout["videoMode"]!.GetValue<string>(), Is.EqualTo("Motion"));
        await using var api = new ApiScope(new StubHandler(ProviderVideoResponse));
        BeutlApiApplication clients = api.Clients;
        await using var group = new AiVideoEditingViewModel(mode => CreateVideoGenerationDialog(clients, sourceMode: mode));
        foreach (var task in group.Tasks)
        {
            group.SelectedTask.Value = task;
            await WaitUntilAsync(() => group.ActiveContent.Value!.ModelPicker.IsLoaded.Value);
            Assert.That(group.ActiveContent.Value!.InputError.Value, Is.Null);
            Assert.That(group.ActiveContent.Value!.CanGenerate.Value, Is.False);
            var view = new AiVideoEditingView { DataContext = group };
            var window = new Window { Content = view, Width = width, Height = 1000, RequestedThemeVariant = width == 320 ? ThemeVariant.Dark : ThemeVariant.Light };
            try
            {
                window.Show(); HeadlessTestHelpers.Render();
                Assert.That(view.FindControl<ComboBox>("TaskPicker")!.Items.Count, Is.EqualTo(3));
                foreach (var scroll in view.GetLogicalDescendants().OfType<ScrollViewer>())
                    Assert.That(scroll.Extent.Width, Is.LessThanOrEqualTo(scroll.Viewport.Width + 1));
                Assert.That(view.GetLogicalDescendants().OfType<AiPromptTemplatesView>(), Is.Empty);
                Assert.That(view.GetLogicalDescendants().OfType<AiPromptHistoryButton>(), Is.Empty);
                Assert.That(view.GetLogicalDescendants().OfType<ScrollViewer>().Count(scroll => scroll.Parent is AiVideoEditingView), Is.EqualTo(1));
                var sourceField = view.GetLogicalDescendants().OfType<StackPanel>().Single(panel => panel.Name == "SourceField");
                var modelField = view.GetLogicalDescendants().OfType<StackPanel>().Single(panel => panel.Name == "ModelField");
                var promptField = view.GetLogicalDescendants().OfType<StackPanel>().Single(panel => panel.Name == "PromptField");
                double Top(Control control) => control.TranslatePoint(default, view)!.Value.Y;
                Assert.That(Top(sourceField), Is.LessThan(Top(view.FindControl<StackPanel>("TaskField")!)));
                Assert.That(Top(modelField), Is.LessThanOrEqualTo(Top(promptField)));
            }
            finally { window.Close(); }
        }
    }
}
