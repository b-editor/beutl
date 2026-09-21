using System.Globalization;
using Avalonia.Headless.NUnit;
using Beutl.Api;
using Beutl.Api.Services;
using Beutl.Language;
using Beutl.Services.AI;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

public sealed partial class AiDialogWorkflowTests
{
    [AvaloniaTest]
    [TestCase(true, "delete")]
    [TestCase(true, "oversize")]
    [TestCase(true, "empty")]
    [TestCase(false, "delete")]
    [TestCase(false, "oversize")]
    [TestCase(false, "empty")]
    public async Task Review_VideoInputChangesKeepSpecificValidationErrors(bool sourceVideo, string change)
    {
        await TestReset.ResetShellAsync();
        int uploads = 0;
        using var handler = new StubHandler(request =>
        {
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath.StartsWith("/api/v3/ai/videos", StringComparison.Ordinal))
                uploads++;
            return ProviderVideoResponse(request);
        });
        using var http = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(http, new ExtensionProvider());
        SetAuthenticatedUser(clients, http);
        using var store = new FileAiRequestRecoveryStore(Path.Combine(BeutlHomeIsolation.CurrentHome!, "invalid-video-" + Guid.NewGuid().ToString("N")));
        using var context = new AiRequestRecoveryContext(store, () => new AiAuthenticatedRequestIdentity("test-user", null));
        await using var vm = CreateVideoGenerationDialog(clients, context: context, sourceMode: sourceVideo ? AiSourceVideoMode.Edit : null);
        await WaitUntilAsync(() => vm.ModelPicker.IsLoaded.Value);
        string path = Path.Combine(BeutlHomeIsolation.CurrentHome!, "changed-input.mp4");
        await File.WriteAllBytesAsync(path, [1, 2, 3]);
        vm.InputPicker = (_, _) => Task.FromResult<IReadOnlyList<string>>([path]);
        vm.VideoDurationReader = _ => TimeSpan.FromSeconds(7);
        if (sourceVideo) await vm.SelectSourceVideo.ExecuteAsync();
        else await vm.ReferenceGroups.Single(group => group.Kind == "video").Pick.ExecuteAsync();
        vm.Prompt.Value = "change the sky";
        await WaitUntilAsync(() => vm.CanGenerate.Value);
        if (change == "delete") File.Delete(path);
        else
        {
            using var file = File.OpenWrite(path);
            file.SetLength(change == "oversize" ? AiVideoInputLimits.MaxSourceBytes + 1 : 0);
        }

        await vm.Generate.ExecuteAsync();

        string expected = change == "oversize" ? Strings.AiFileTooLarge : Strings.AiVideoInputUnavailable;
        Assert.That(vm.InputError.Value, Is.EqualTo(expected));
        Assert.That(vm.Error.Value, Is.EqualTo(expected));
        Assert.That(vm.IsGenerating.Value, Is.False);
        Assert.That(uploads, Is.Zero);
        Assert.That(store.PendingFor("test-user", sourceVideo ? "video.edit" : "video.generate"), Is.Empty);
    }

    [AvaloniaTest]
    [TestCase(1, false)]
    [TestCase(11, false)]
    [TestCase(7.2, true)]
    public async Task Review_SourceSnapshotRevalidatesChangedDuration(double replacementSeconds, bool enforceBudget)
    {
        await TestReset.ResetShellAsync();
        int uploads = 0;
        using var handler = new StubHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/v3/ai/videos/edit") uploads++;
            return ProviderVideoResponse(request);
        });
        using var http = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(http, new ExtensionProvider());
        SetAuthenticatedUser(clients, http);
        using var store = new FileAiRequestRecoveryStore(Path.Combine(BeutlHomeIsolation.CurrentHome!, "source-snapshot-" + Guid.NewGuid().ToString("N")));
        using var context = new AiRequestRecoveryContext(store, () => new AiAuthenticatedRequestIdentity("test-user", null));
        var availability = new SourceDurationAvailability();
        await using var vm = CreateVideoGenerationDialog(clients, context: context, sourceMode: AiSourceVideoMode.Edit,
            availability: enforceBudget ? availability : null);
        string path = Path.Combine(BeutlHomeIsolation.CurrentHome!, "replaced-source.mp4");
        await File.WriteAllTextAsync(path, "2.2");
        vm.InputPicker = (_, _) => Task.FromResult<IReadOnlyList<string>>([path]);
        string? snapshotPath = null;
        vm.VideoDurationReader = probePath =>
        {
            if (probePath != path) snapshotPath = probePath;
            return TimeSpan.FromSeconds(double.Parse(File.ReadAllText(probePath), CultureInfo.InvariantCulture));
        };
        await vm.SelectSourceVideo.ExecuteAsync();
        vm.Prompt.Value = "edit the sky";
        await WaitUntilAsync(() => vm.CanGenerate.Value);
        await File.WriteAllTextAsync(path, replacementSeconds.ToString(CultureInfo.InvariantCulture));

        await vm.Generate.ExecuteAsync();

        Assert.That(vm.SourceDuration.Value, Is.EqualTo(replacementSeconds));
        Assert.That(uploads, Is.Zero);
        Assert.That(store.PendingFor("test-user", "video.edit"), Is.Empty);
        Assert.That(vm.Error.Value, Is.EqualTo(enforceBudget ? Strings.AiUsageLimitExceeded : Strings.AiModelDoesNotSupportRequest));
        if (enforceBudget) Assert.That(availability.Durations[^1], Is.EqualTo(8));
        Assert.That(snapshotPath, Is.Not.Null);
        Assert.That(File.Exists(snapshotPath), Is.False);
    }
}
