using System.Globalization;
using System.Net;
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
    [TestCase(AiSourceVideoMode.Edit, 7.2, false)]
    [TestCase(AiSourceVideoMode.Extend, 12.2, false)]
    [TestCase(AiSourceVideoMode.Edit, 7.2, true)]
    [TestCase(AiSourceVideoMode.Extend, 12.2, true)]
    public async Task Review_SourceVideoImportRetainsTheFullResultDuration(AiSourceVideoMode mode, double expectedSeconds, bool deleteSource)
    {
        await TestReset.ResetShellAsync();
        var editor = await OpenEditor("fractional-video-edit");
        string? uploaded = null;
        using var handler = new StubHandler(async (request, token) =>
        {
            if (request.RequestUri?.AbsolutePath is "/api/v3/ai/videos/edit" or "/api/v3/ai/videos/extend")
                uploaded = await request.Content!.ReadAsStringAsync(token);
            return request.RequestUri?.AbsolutePath switch
            {
                "/api/v3/ai/videos/edit" or "/api/v3/ai/videos/extend" => JsonResponse(HttpStatusCode.OK, "{\"jobId\":\"edited\",\"status\":\"queued\"}"),
                "/api/v3/ai/videos/edited" => JsonResponse(HttpStatusCode.OK, """
                    {"jobId":"edited","status":"succeeded","fileId":"edited-file","url":"https://beutl.beditor.net/api/contents/edited-file","fileName":"edited.mp4","contentType":"video/mp4"}
                    """),
                "/api/contents/edited-file" => ByteResponse([1, 2, 3, 4], "video/mp4"),
                _ => ProviderVideoResponse(request),
            };
        });
        using var http = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(http, new ExtensionProvider());
        SetAuthenticatedUser(clients, http);
        await using var vm = CreateVideoGenerationDialog(clients, editor, sourceMode: mode);
        string path = Path.Combine(BeutlHomeIsolation.CurrentHome!, "fractional-source.mp4");
        await File.WriteAllTextAsync(path, "2.2");
        vm.InputPicker = (_, _) => Task.FromResult<IReadOnlyList<string>>([path]);
        int probes = 0;
        string? snapshotPath = null;
        vm.VideoDurationReader = probePath =>
        {
            if (++probes == 2)
            {
                snapshotPath = probePath;
                // A concurrent render or deletion must not invalidate the captured upload.
                if (deleteSource) File.Delete(path);
                else
                {
                    using var rewritten = File.OpenWrite(path);
                    rewritten.SetLength(AiVideoInputLimits.MaxSourceBytes + 1);
                }
            }
            return TimeSpan.FromSeconds(double.Parse(File.ReadAllText(probePath), CultureInfo.InvariantCulture));
        };
        await vm.SelectSourceVideo.ExecuteAsync();
        Assert.That(vm.SourceDuration.Value, Is.EqualTo(2.2));
        await WaitUntilAsync(() => vm.ModelPicker.IsLoaded.Value);
        if (mode == AiSourceVideoMode.Extend)
            vm.SelectedDuration.Value = vm.DurationOptions.Single(option => option.Seconds == 5);
        vm.Prompt.Value = "change the sky";
        await WaitUntilAsync(() => vm.CanGenerate.Value);
        await File.WriteAllTextAsync(path, "7.2");
        await vm.Generate.ExecuteAsync();
        Assert.That(vm.ResultVideoPath.Value, Is.Not.Null, vm.Error.Value);
        Assert.That(uploaded, Does.Contain("7.2").And.Not.Contain("9.8"));
        Assert.That(vm.SourceDuration.Value, Is.EqualTo(7.2));
        Assert.That(snapshotPath, Is.Not.Null.And.Not.EqualTo(path));
        Assert.That(File.Exists(snapshotPath), Is.False);
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
        Assert.That(imported!.Length, Is.EqualTo(TimeSpan.FromSeconds(expectedSeconds)));
    }
}
