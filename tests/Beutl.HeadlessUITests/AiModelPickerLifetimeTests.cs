using System.Collections.Immutable;
using Avalonia.Headless.NUnit;
using Beutl.Api.Services;
using Beutl.ViewModels;
using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public sealed class AiModelPickerLifetimeTests
{
    [AvaloniaTest]
    public async Task Dispose_DropsACatalogResultThatIgnoresCancellation()
    {
        var catalog = new BlockingCatalog();
        using var entitlements = new StubEntitlements();
        var picker = new AiModelPickerViewModel(catalog, entitlements);

        Task load = picker.LoadAsync(AiOperations.ImageGeneration, CancellationToken.None);
        await catalog.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        picker.Dispose();
        catalog.Release.TrySetResult(AiModelCatalog.Empty);
        await load.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(picker.Options, Is.Empty);
            Assert.That(picker.Operation, Is.EqualTo(default(AiOperationId)));
        });
    }

    [AvaloniaTest]
    public async Task Load_SelectsTheServerDefaultBeforeAnEarlierModel()
    {
        var catalog = new AiModelCatalog(
        [
            KeyValuePair.Create(
                AiOperations.ImageGeneration,
                ImmutableArray.Create(
                    new AiModelOption(new AiModelId("first"), "First", null, false),
                    new AiModelOption(new AiModelId("default"), "Default", null, true))),
        ]);
        using var entitlements = new StubEntitlements();
        using var picker = new AiModelPickerViewModel(new FixedCatalog(catalog), entitlements);

        await picker.LoadAsync(AiOperations.ImageGeneration, CancellationToken.None);

        Assert.That(picker.SelectedModel, Is.EqualTo(new AiModelId("default")));
    }

    private sealed class BlockingCatalog : IAiModelCatalogService
    {
        public TaskCompletionSource Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<AiModelCatalog> Release { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<AiModelCatalog> GetAsync(CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            return await Release.Task;
        }

        public void Invalidate()
        {
        }
    }

    private sealed class FixedCatalog(AiModelCatalog catalog) : IAiModelCatalogService
    {
        public Task<AiModelCatalog> GetAsync(CancellationToken cancellationToken)
            => Task.FromResult(catalog);

        public void Invalidate()
        {
        }
    }

    private sealed class StubEntitlements : IAiEntitlementService, IDisposable
    {
        private readonly ReactivePropertySlim<AiEntitlements?> _entitlements = new();

        public IReadOnlyReactiveProperty<AiEntitlements?> Entitlements => _entitlements;

        public Task<AiEntitlements?> RefreshAsync(CancellationToken cancellationToken)
            => Task.FromResult(_entitlements.Value);

        public void Dispose() => _entitlements.Dispose();
    }
}
