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
    public async Task Review_CachedEditTaskRefreshesAvailabilityAfterBalanceChanges()
    {
        await TestReset.ResetShellAsync();
        var availability = new MutableTaskAvailability();
        using var handler = new StubHandler(ProviderVideoResponse);
        using var http = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(http, new ExtensionProvider());
        SetAuthenticatedUser(clients, http);
        await using var group = new AiVideoEditingViewModel(mode => CreateVideoGenerationDialog(clients, sourceMode: mode, availability: availability));
        var edit = group.ActiveContent.Value!;
        string path = Path.Combine(BeutlHomeIsolation.CurrentHome!, "cached-edit-source.mp4");
        await File.WriteAllBytesAsync(path, [1, 2, 3]);
        edit.InputPicker = (_, _) => Task.FromResult<IReadOnlyList<string>>([path]);
        edit.VideoDurationReader = _ => TimeSpan.FromSeconds(7);
        await edit.SelectSourceVideo.ExecuteAsync();
        edit.Prompt.Value = "edit the sky";
        await WaitUntilAsync(() => edit.ModelPicker.IsLoaded.Value && edit.EstimatedUsage.State.Value == AiOperationAvailabilityState.Unavailable);
        Assert.That(edit.CanGenerate.Value, Is.False);

        group.SelectedTask.Value = group.Tasks.Single(task => task.Mode == AiSourceVideoMode.Extend);
        var extend = group.ActiveContent.Value!;
        await WaitUntilAsync(() => extend.ModelPicker.IsLoaded.Value && extend.EstimatedUsage.State.Value == AiOperationAvailabilityState.Unavailable);
        availability.Available = true;
        extend.RefreshAvailability();
        await WaitUntilAsync(() => extend.EstimatedUsage.State.Value == AiOperationAvailabilityState.Available);

        group.SelectedTask.Value = group.Tasks[0];

        Assert.That(group.ActiveContent.Value, Is.SameAs(edit));
        await WaitUntilAsync(() => edit.EstimatedUsage.State.Value == AiOperationAvailabilityState.Available);
        Assert.That(edit.CanGenerate.Value, Is.True);
    }

    private sealed class MutableTaskAvailability : IAiOperationAvailabilityService
    {
        public bool Available { get; set; }
        public Task<bool> CheckAsync(AiOperationAvailabilityRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Available);
        }
    }

    [AvaloniaTest]
    public async Task Review_CachedEditTaskReloadsWithdrawnModels()
    {
        await TestReset.ResetShellAsync();
        string capabilities = ProviderVideoCapabilities;
        using var handler = new StubHandler(request => request.RequestUri?.AbsolutePath == "/api/v3/ai/capabilities"
            ? JsonResponse(HttpStatusCode.OK, capabilities) : ProviderVideoResponse(request));
        using var http = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(http, new ExtensionProvider());
        SetAuthenticatedUser(clients, http);
        await using var group = new AiVideoEditingViewModel(mode => CreateVideoGenerationDialog(clients, sourceMode: mode));
        var edit = group.ActiveContent.Value!;
        await WaitUntilAsync(() => edit.ModelPicker.IsLoaded.Value);
        Assert.That(edit.ModelPicker.SelectedModel?.Value, Is.EqualTo("gateway/editor"));
        group.SelectedTask.Value = group.Tasks.Single(task => task.Mode == AiSourceVideoMode.Extend);
        await WaitUntilAsync(() => group.ActiveContent.Value!.ModelPicker.IsLoaded.Value);
        capabilities = capabilities.Replace("gateway/editor", "gateway/replacement");
        clients.GetResource<IAiModelCatalogService>().Invalidate();
        group.SelectedTask.Value = group.Tasks[0];
        Assert.That(group.ActiveContent.Value, Is.SameAs(edit));
        await WaitUntilAsync(() => edit.ModelPicker.Options.Any(option => option.Id.Value == "gateway/replacement"));
        Assert.That(edit.ModelPicker.Options.Any(option => option.Id.Value == "gateway/editor"), Is.False);
    }
}
