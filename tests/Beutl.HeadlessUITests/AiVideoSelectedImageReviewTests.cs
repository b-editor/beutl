using System.Net;
using Avalonia.Headless.NUnit;
using Beutl.Api;
using Beutl.Api.Services;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Graphics;
using Beutl.Media.Source;
using Beutl.Services.AI;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

public sealed partial class AiDialogWorkflowTests
{
    [AvaloniaTest]
    [TestCase(null)]
    [TestCase(AiSourceVideoMode.Edit)]
    [TestCase(AiSourceVideoMode.Extend)]
    [TestCase(AiSourceVideoMode.Motion)]
    public async Task Review_SelectedSceneImageOnlyInitializesVideoGeneration(AiSourceVideoMode? mode)
    {
        await TestReset.ResetShellAsync();
        var editor = await OpenEditor("selected-image-video");
        string imagePath = Path.Combine(BeutlHomeIsolation.CurrentHome!, "unrelated-selection.png");
        await File.WriteAllBytesAsync(imagePath, s_png);
        var imageSource = new ImageSource();
        imageSource.ReadFrom(new Uri(imagePath));
        var image = new SourceImage();
        image.Source.CurrentValue = imageSource;
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        var added = await adder.AddAsync([new ElementDescription(TimeSpan.Zero, TimeSpan.FromSeconds(2), 0,
            new ElementSource.EngineObject(() => image))], CancellationToken.None);
        Assert.That(added.IsSuccess, Is.True, added.Failure?.Message);
        var selected = added.Elements.Single();
        ((IEditorSelection)editor.GetService(typeof(IEditorSelection))!).SelectedObject.Value = selected;
        var sent = new List<(string Key, string Body)>();
        using var handler = new StubHandler(async (request, token) =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/v3/ai/capabilities")
                return JsonResponse(HttpStatusCode.OK, ProviderVideoCapabilities.Replace("\"inputReferences\":true", "\"inputReferences\":true,\"firstFrame\":true"));
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath.StartsWith("/api/v3/ai/videos/", StringComparison.Ordinal))
                sent.Add((request.Headers.GetValues("Idempotency-Key").Single(), await request.Content!.ReadAsStringAsync(token)));
            return ProviderVideoResponse(request);
        });
        using var http = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(http, new ExtensionProvider());
        SetAuthenticatedUser(clients, http);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        using var store = new FileAiRequestRecoveryStore(Path.Combine(BeutlHomeIsolation.CurrentHome!, "selected-image-" + Guid.NewGuid().ToString("N")), () => now);
        using var context = new AiRequestRecoveryContext(store, () => new AiAuthenticatedRequestIdentity("test-user", null));
        await using (var vm = CreateVideoGenerationDialog(clients, editor, context: context, sourceMode: mode))
        {
            await WaitUntilAsync(() => vm.ModelPicker.IsLoaded.Value);
            if (mode is null)
            {
                Assert.That(vm.FirstFramePath.Value, Is.EqualTo(imagePath));
                return;
            }
            Assert.That(vm.FirstFramePath.Value, Is.Null);
            Assert.That(vm.LastFramePath.Value, Is.Null);
            File.Delete(imagePath);
            string videoPath = Path.Combine(BeutlHomeIsolation.CurrentHome!, "selected-source.mp4");
            string characterPath = Path.Combine(BeutlHomeIsolation.CurrentHome!, "selected-character.png");
            await File.WriteAllBytesAsync(videoPath, [1, 2, 3]);
            await File.WriteAllBytesAsync(characterPath, s_png);
            vm.InputPicker = (role, _) => Task.FromResult<IReadOnlyList<string>>([role == "source" ? videoPath : characterPath]);
            vm.VideoDurationReader = _ => TimeSpan.FromSeconds(7);
            await vm.SelectSourceVideo.ExecuteAsync();
            if (mode == AiSourceVideoMode.Motion) await vm.SelectCharacterImage.ExecuteAsync();
            vm.Prompt.Value = "edit the sky";
            await WaitUntilAsync(() => vm.CanGenerate.Value);
            await vm.Generate.ExecuteAsync();
            Assert.That(sent, Has.Count.EqualTo(1), vm.Error.Value);
            Assert.That(sent[0].Body, Does.Not.Contain("unrelated-selection.png"));
            var pending = store.PendingFor("test-user", "video." + mode.Value.ToString().ToLowerInvariant()).Single();
            Assert.That(pending.EffectiveSources.Select(source => source.Role), Is.EquivalentTo(mode == AiSourceVideoMode.Motion
                ? new[] { "source-video", "character-image" } : ["source-video"]));
            Assert.That(pending.Form!.FirstFrameElementId, Is.Null);
            Assert.That(pending.Form.LastFrameElementId, Is.Null);
        }
        now = now.AddMinutes(16);
        await using var restored = CreateVideoGenerationDialog(clients, editor, context: context, sourceMode: mode);
        await WaitUntilAsync(() => restored.CanGenerate.Value);
        Assert.That(restored.FirstFramePath.Value, Is.Null);
        Assert.That(restored.LastFramePath.Value, Is.Null);
        await restored.Generate.ExecuteAsync();
        Assert.That(sent, Has.Count.EqualTo(2), restored.Error.Value);
        Assert.That(sent[1].Key, Is.EqualTo(sent[0].Key));
    }
}
