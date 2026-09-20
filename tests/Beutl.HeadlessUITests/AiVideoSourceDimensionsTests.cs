using System.Net;
using System.Text.Json.Nodes;
using Avalonia.Headless.NUnit;
using Beutl.Api;
using Beutl.Api.Services;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

public sealed partial class AiDialogWorkflowTests
{
    [AvaloniaTest]
    [TestCase(AiSourceVideoMode.Edit, false, true)]
    [TestCase(AiSourceVideoMode.Extend, true, true)]
    [TestCase(AiSourceVideoMode.Motion, true, true)]
    [TestCase(AiSourceVideoMode.Extend, false, false)]
    [TestCase(AiSourceVideoMode.Motion, false, false)]
    [TestCase(null, true, false)]
    public async Task SourceDimensions_RequireOnlyFieldsUsedByTheOperation(AiSourceVideoMode? mode, bool durations, bool usable)
    {
        await TestReset.ResetShellAsync();
        string operation = mode switch
        {
            AiSourceVideoMode.Edit => "video.edit", AiSourceVideoMode.Extend => "video.extend",
            AiSourceVideoMode.Motion => "video.motion", _ => "video.generate",
        };
        var capabilities = JsonNode.Parse(ProviderVideoCapabilities)!;
        capabilities["operations"]![operation]!["models"] = new JsonArray(new JsonObject
        {
            ["id"] = "gateway/source-model", ["isDefault"] = true,
            ["durationsSeconds"] = durations ? new JsonArray(5, 10) : new JsonArray(),
            ["resolutions"] = new JsonArray(), ["aspectRatios"] = new JsonArray(),
        });
        string? sent = null;
        using var handler = new StubHandler(async (request, token) =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/v3/ai/capabilities")
                return JsonResponse(HttpStatusCode.OK, capabilities.ToJsonString());
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath.StartsWith("/api/v3/ai/videos", StringComparison.Ordinal))
                sent = await request.Content!.ReadAsStringAsync(token);
            return ProviderVideoResponse(request);
        });
        using var http = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(http, new ExtensionProvider());
        SetAuthenticatedUser(clients, http);
        await using var vm = CreateVideoGenerationDialog(clients, sourceMode: mode);
        await WaitUntilAsync(() => vm.ModelPicker.IsLoaded.Value);
        Assert.That(vm.ModelPicker.Options.Count, Is.EqualTo(usable ? 1 : 0));
        Assert.That(vm.DurationOptions, Is.Not.Empty);
        Assert.That(vm.ResolutionOptions, Is.Not.Empty);
        Assert.That(vm.AspectRatioOptions, Is.Not.Empty);
        if (!usable) return;
        string source = Path.Combine(BeutlHomeIsolation.CurrentHome!, "source.mp4");
        string character = Path.Combine(BeutlHomeIsolation.CurrentHome!, "character.png");
        await File.WriteAllBytesAsync(source, [1, 2, 3]);
        await File.WriteAllBytesAsync(character, [1, 2, 3]);
        vm.InputPicker = (role, _) => Task.FromResult<IReadOnlyList<string>>([role == "source" ? source : character]);
        vm.VideoDurationReader = _ => TimeSpan.FromSeconds(7);
        await vm.SelectSourceVideo.ExecuteAsync();
        if (mode == AiSourceVideoMode.Motion) await vm.SelectCharacterImage.ExecuteAsync();
        vm.Prompt.Value = "edit the clip";
        await WaitUntilAsync(() => vm.CanGenerate.Value);
        await vm.Generate.ExecuteAsync();
        Assert.That(sent, Is.Not.Null, vm.Error.Value);
        Assert.That(sent, Does.Not.Contain("name=resolution").And.Not.Contain("name=aspectRatio"));
        if (mode == AiSourceVideoMode.Edit) Assert.That(sent, Does.Not.Contain("durationSeconds"));
        else Assert.That(sent, Does.Contain("durationSeconds"));
    }
}
