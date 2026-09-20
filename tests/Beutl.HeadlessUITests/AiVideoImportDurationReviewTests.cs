using System.Net;
using System.Text.Json;
using Avalonia.Headless.NUnit;
using Beutl.Api;
using Beutl.Api.Services;
using Beutl.Services.AI;
using Beutl.Testing.Headless;
using Beutl.ViewModels.Dialogs;

namespace Beutl.HeadlessUITests;

public sealed partial class AiDialogWorkflowTests
{
    [AvaloniaTest]
    public async Task Review_EditImportRetainsExactDurationFromTheSubmittedSource()
    {
        await TestReset.ResetShellAsync();
        var editor = await OpenEditor("fractional-video-edit");
        using var handler = new StubHandler(request => request.RequestUri?.AbsolutePath switch
        {
            "/api/v3/ai/videos/edit" => JsonResponse(HttpStatusCode.OK, "{\"jobId\":\"edited\",\"status\":\"queued\"}"),
            "/api/v3/ai/videos/edited" => JsonResponse(HttpStatusCode.OK, """
                {"jobId":"edited","status":"succeeded","fileId":"edited-file","url":"https://beutl.beditor.net/api/contents/edited-file","fileName":"edited.mp4","contentType":"video/mp4"}
                """),
            "/api/contents/edited-file" => ByteResponse([1, 2, 3, 4], "video/mp4"),
            _ => ProviderVideoResponse(request),
        });
        using var http = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(http, new ExtensionProvider());
        SetAuthenticatedUser(clients, http);
        await using var vm = CreateVideoGenerationDialog(clients, editor, sourceMode: AiSourceVideoMode.Edit);
        string path = Path.Combine(BeutlHomeIsolation.CurrentHome!, "fractional-source.mp4");
        await File.WriteAllBytesAsync(path, [1, 2, 3]);
        vm.InputPicker = (_, _) => Task.FromResult<IReadOnlyList<string>>([path]);
        vm.VideoDurationReader = _ => TimeSpan.FromSeconds(7.2);
        await vm.SelectSourceVideo.ExecuteAsync();
        vm.Prompt.Value = "change the sky";
        await WaitUntilAsync(() => vm.CanGenerate.Value);
        await vm.Generate.ExecuteAsync();
        Assert.That(vm.ResultVideoPath.Value, Is.Not.Null, vm.Error.Value);
        // Later edits to the input must not change the completed result's length.
        vm.SourceDuration.Value = 3.5;
        AiResultImportOptions? imported = null;
        vm.ResultImporter = (_, options, _) =>
        {
            imported = options;
            return Task.FromResult(Beutl.Editor.Services.ElementAddResult.Failed(
                new Beutl.Editor.Services.ElementMaterializationFailure("Import intercepted for test.")));
        };
        await vm.AddToScene.ExecuteAsync();
        Assert.That(imported, Is.Not.Null);
        Assert.That(imported!.Length, Is.EqualTo(TimeSpan.FromSeconds(7.2)));
    }
}
