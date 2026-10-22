using System.Net;
using Avalonia.Headless.NUnit;
using Beutl.Api;
using Beutl.Api.Services;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.Graphics;
using Beutl.Media.Source;
using Beutl.Services.AI;
using Beutl.Testing.Headless;
using static Beutl.HeadlessUITests.AiTestSupport;

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
        (_, _, var editor) = await TestShell.OpenNewSceneAsync("selected-image-video");
        string imagePath = Path.Combine(BeutlHomeIsolation.CurrentHome!, "unrelated-selection.png");
        await File.WriteAllBytesAsync(imagePath, s_png);
        var imageSource = new ImageSource();
        imageSource.ReadFrom(new Uri(imagePath));
        var image = new SourceImage();
        image.Source.Value = imageSource;
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        var added = await adder.AddAsync([new ElementDescription(MediaTime.Zero, MediaTime.FromSeconds(2), 0,
            new ElementSource.EngineObject(() => image))], CancellationToken.None);
        Assert.That(added.IsSuccess, Is.True, added.Failure?.Message);
        var selected = added.Elements.Single();
        ((IEditorSelection)editor.GetService(typeof(IEditorSelection))!).Select(selected);
        var sent = new List<(string Key, string Body)>();
        await using var api = new ApiScope(new StubHandler(async (request, token) =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/v3/ai/capabilities")
                return JsonResponse(HttpStatusCode.OK, ProviderVideoCapabilities.Replace("\"inputReferences\":true", "\"inputReferences\":true,\"firstFrame\":true"));
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath.StartsWith("/api/v3/ai/videos/", StringComparison.Ordinal))
                sent.Add((request.Headers.GetValues("Idempotency-Key").Single(), await request.Content!.ReadAsStringAsync(token)));
            return ProviderVideoResponse(request);
        }));
        BeutlApiApplication clients = api.Clients;
        await using (var vm = CreateVideoGenerationDialog(clients, editor, sourceMode: mode))
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
        }
    }
}
