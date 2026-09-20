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
