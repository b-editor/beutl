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
    [TestCase(2.2, 3, true)]
    [TestCase(7.2, 8, false)]
    public async Task Review_EditRefreshPricesTheSourceDuration(double sourceSeconds, int expectedSeconds, bool affordable)
    {
        await TestReset.ResetShellAsync();
        var availability = new SourceDurationAvailability();
        var durations = availability.Durations;
        using var handler = new StubHandler(ProviderVideoResponse);
        using var http = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(http, new ExtensionProvider());
        SetAuthenticatedUser(clients, http);
        await using var vm = CreateVideoGenerationDialog(clients, sourceMode: AiSourceVideoMode.Edit, availability: availability);
        await WaitUntilAsync(() => vm.ModelPicker.IsLoaded.Value);
        string path = Path.Combine(BeutlHomeIsolation.CurrentHome!, "refresh-source.mp4");
        await File.WriteAllBytesAsync(path, [1, 2, 3]);
        vm.InputPicker = (_, _) => Task.FromResult<IReadOnlyList<string>>([path]);
        vm.VideoDurationReader = _ => TimeSpan.FromSeconds(sourceSeconds);
        await vm.SelectSourceVideo.ExecuteAsync();
        await WaitUntilAsync(() => vm.EstimatedUsage.State.Value != AiOperationAvailabilityState.Unknown);
        int before = durations.Count;
        vm.RefreshAvailability();
        await WaitUntilAsync(() => durations.Count > before && vm.EstimatedUsage.State.Value != AiOperationAvailabilityState.Unknown);
        Assert.That(durations[^1], Is.EqualTo(expectedSeconds));
        Assert.That(vm.EstimatedUsage.CanAfford.Value, Is.EqualTo(affordable));
    }
    private sealed class SourceDurationAvailability : IAiOperationAvailabilityService
    {
        public List<int> Durations { get; } = [];
        public Task<bool> CheckAsync(AiOperationAvailabilityRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int seconds = ((AiOperationAvailabilityRequest.Video)request).DurationSeconds;
            Durations.Add(seconds);
            return Task.FromResult(seconds <= 4);
        }
    }
}
