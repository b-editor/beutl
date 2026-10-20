using System.Net;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Beutl.Api;
using Beutl.Api.Services;
using Beutl.Language;
using Beutl.Services.AI;
using Beutl.Testing.Headless;
using Beutl.Views.Tools;
using static Beutl.HeadlessUITests.AiTestSupport;

namespace Beutl.HeadlessUITests;

public sealed partial class AiDialogWorkflowTests
{
    private const string ProviderVideoCapabilities = """
        {"operations":{
          "video.generate":{"models":[{"id":"gateway/model","isDefault":true,"durationsSeconds":[4,6,8],"resolutions":["720p"],"aspectRatios":["16:9"],"inputReferences":true,"maxInputReferences":9,"maxInputReferenceBytes":5242880,"maxVideoReferences":3,"maxVideoReferenceBytes":33554432,"maxAudioReferences":1,"maxAudioReferenceBytes":15728640},{"id":"legacy/model"}]},
          "video.edit":{"models":[{"id":"gateway/editor","isDefault":true,"maxPromptLength":80,"maxSourceVideoBytes":33554432,"minSourceVideoSeconds":2,"maxSourceVideoSeconds":10}]},
          "video.extend":{"models":[{"id":"gateway/extend","isDefault":true,"durationsSeconds":[5,10]}]},
          "video.motion":{"models":[{"id":"gateway/motion","isDefault":true,"durationsSeconds":[5,10]}]}
        }}
        """;
    private static HttpResponseMessage ProviderVideoResponse(HttpRequestMessage request) => request.RequestUri?.AbsolutePath switch
    {
        "/api/v3/user/entitlements" => JsonResponse(HttpStatusCode.OK, EntitlementsJson().Replace("\"video.generate\": true", "\"video.generate\": true, \"video.edit\": true, \"video.extend\": true, \"video.motion\": true")),
        "/api/v3/ai/capabilities" => JsonResponse(HttpStatusCode.OK, ProviderVideoCapabilities),
        "/api/v3/user/ai-availability" => JsonResponse(HttpStatusCode.OK, """{"available":true}"""),
        _ => JsonResponse(HttpStatusCode.Conflict, """{"error_code":"aiRequestInProgress","message":"Pending"}"""),
    };

    [AvaloniaTest]
    public async Task ProviderInputs_PickKindsInWebOrder()
    {
        await TestReset.ResetShellAsync();
        var requests = new List<(string Key, string Body)>();
        await using var api = new ApiScope(new StubHandler(async (request, token) =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/v3/ai/videos/frames")
                requests.Add((request.Headers.GetValues("Idempotency-Key").Single(), await request.Content!.ReadAsStringAsync(token)));
            return ProviderVideoResponse(request);
        }));
        BeutlApiApplication clients = api.Clients;
        await using (var vm = CreateVideoGenerationDialog(clients))
        {
            await WaitUntilAsync(() => vm.ReferenceGroups.All(group => group.IsSupported.Value));
            var files = new Dictionary<string, string>();
            foreach (var (kind, extension) in new[] { ("audio", "wave"), ("video", "webm"), ("image", "png") })
            {
                string path = Path.Combine(BeutlHomeIsolation.CurrentHome!, "reference-" + kind + "." + extension);
                await File.WriteAllBytesAsync(path, [1, 2, 3]);
                files[kind] = path;
            }
            vm.InputPicker = (kind, _) => Task.FromResult<IReadOnlyList<string>>([files[kind]]);
            foreach (string kind in new[] { "audio", "video", "image" })
                await vm.ReferenceGroups.Single(group => group.Kind == kind).Pick.ExecuteAsync();
            vm.Prompt.Value = "animate the references";
            await WaitUntilAsync(() => vm.CanGenerate.Value);
            await vm.Generate.ExecuteAsync();
            Assert.That(requests, Has.Count.EqualTo(1), vm.Error.Value);
            Assert.That(requests[0].Body, Does.Contain("audio/wav").And.Contain("video/webm").And.Contain("image/png"));
            Assert.That(requests[0].Body.IndexOf("reference-image"), Is.LessThan(requests[0].Body.IndexOf("reference-video")));
            Assert.That(requests[0].Body.IndexOf("reference-video"), Is.LessThan(requests[0].Body.IndexOf("reference-audio")));
        }
    }

    [AvaloniaTest]
    public async Task ProviderInputs_ModelChangesKeepSelectionsAndFirstFramesSuspendReferences()
    {
        await TestReset.ResetShellAsync();
        await using var api = new ApiScope(new StubHandler(ProviderVideoResponse));
        BeutlApiApplication clients = api.Clients;
        await using var vm = CreateVideoGenerationDialog(clients);
        await WaitUntilAsync(() => vm.ModelPicker.Options.Count == 2);
        string path = Path.Combine(BeutlHomeIsolation.CurrentHome!, "input.mp4");
        await File.WriteAllBytesAsync(path, [1, 2, 3]);
        vm.InputPicker = (_, _) => Task.FromResult<IReadOnlyList<string>>([path]);
        var video = vm.ReferenceGroups.Single(group => group.Kind == "video");
        await video.Pick.ExecuteAsync();
        vm.ModelPicker.Selected.Value = vm.ModelPicker.Options.Single(option => option.Id.Value == "legacy/model");
        Assert.That(video.IsVisible.Value, Is.True);
        Assert.That(video.Pick.CanExecute(), Is.False);
        Assert.That(vm.InputError.Value, Is.Not.Null);
        vm.FirstFramePath.Value = "frame.png";
        Assert.That(vm.InputError.Value, Is.Null);
        Assert.That(vm.ReferencesSuspended.Value, Is.True);
        vm.FirstFramePath.Value = null;
        video.Files.Single().Remove.Execute();
        Assert.That(video.IsVisible.Value, Is.False);
        Assert.That(vm.InputError.Value, Is.Null);
    }

    [AvaloniaTest]
    public async Task ProviderInputs_AudioControlsFollowThePublishedAllowance()
    {
        await TestReset.ResetShellAsync();
        await using var api = new ApiScope(new StubHandler(request => request.RequestUri?.AbsolutePath == "/api/v3/ai/capabilities"
            ? JsonResponse(HttpStatusCode.OK, ProviderVideoCapabilities.Replace("\"maxAudioReferences\":1", "\"maxAudioReferences\":0")) : ProviderVideoResponse(request)));
        BeutlApiApplication clients = api.Clients;
        await using var vm = CreateVideoGenerationDialog(clients);
        await WaitUntilAsync(() => vm.ModelPicker.Options.Count == 2);
        Assert.That(vm.ReferenceGroups.Single(group => group.Kind == "audio").IsVisible.Value, Is.False);
    }

    [AvaloniaTest, SetUICulture("ja-JP")]
    [TestCase(320)]
    [TestCase(640)]
    public async Task ProviderInputs_ReferenceControlsFitNarrowTabs(int width)
    {
        await TestReset.ResetShellAsync();
        await using var api = new ApiScope(new StubHandler(ProviderVideoResponse));
        BeutlApiApplication clients = api.Clients;
        await using var vm = CreateVideoGenerationDialog(clients);
        await WaitUntilAsync(() => vm.ReferenceGroups.All(group => group.IsSupported.Value));
        var view = new AiVideoGenerationView { DataContext = vm };
        var window = new Window { Content = view, Width = width, Height = 1100, RequestedThemeVariant = ThemeVariant.Dark };
        try
        {
            window.Show(); HeadlessTestHelpers.Render();
            foreach (var group in vm.ReferenceGroups)
                Assert.That(view.GetLogicalDescendants().OfType<Button>().Single(button => ReferenceEquals(button.Command, group.Pick)).IsEffectivelyVisible, Is.True);
            foreach (var scroll in view.GetLogicalDescendants().OfType<ScrollViewer>())
                Assert.That(scroll.Extent.Width, Is.LessThanOrEqualTo(scroll.Viewport.Width + 1));
        }
        finally { window.Close(); }
    }
}
