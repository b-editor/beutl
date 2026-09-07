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

    [AvaloniaTest]
    public async Task Load_SwitchesToTheReferenceBudgetForTheCurrentOperation()
    {
        var firstOperation = new AiOperationId("vendor.picture.small");
        var secondOperation = new AiOperationId("vendor.picture.large");
        var catalog = new AiModelCatalog(
        [
            KeyValuePair.Create(
                firstOperation,
                ImmutableArray.Create(new AiModelOption(
                    new AiModelId("small"),
                    "Small",
                    null,
                    true))),
            KeyValuePair.Create(
                secondOperation,
                ImmutableArray.Create(new AiModelOption(
                    new AiModelId("large"),
                    "Large",
                    null,
                    true))),
        ],
        imageReferenceLimits:
        [
            KeyValuePair.Create(firstOperation, new AiImageReferenceLimits(2 * 1024 * 1024)),
            KeyValuePair.Create(secondOperation, new AiImageReferenceLimits(18 * 1024 * 1024)),
        ]);
        using var entitlements = new StubEntitlements();
        using var picker = new AiModelPickerViewModel(new FixedCatalog(catalog), entitlements);

        await picker.LoadAsync(firstOperation, CancellationToken.None);
        long firstBudget = picker.ImageReferenceLimits.MaxTotalBytes;
        await picker.LoadAsync(secondOperation, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstBudget, Is.EqualTo(2 * 1024 * 1024));
            Assert.That(picker.Operation, Is.EqualTo(secondOperation));
            Assert.That(picker.ImageReferenceLimits.MaxTotalBytes,
                Is.EqualTo(18 * 1024 * 1024));
        }
    }

    [AvaloniaTest]
    public async Task ReconcileRecoveryModelsReappliesTheCurrentCapabilityFilter()
    {
        var recoveryModel = new AiModelId("recovery-only");
        var catalog = new AiModelCatalog(
        [
            KeyValuePair.Create(
                AiOperations.VideoGeneration,
                ImmutableArray.Create(new AiModelOption(
                    recoveryModel,
                    "Recovery only",
                    null,
                    true))),
        ]);
        using var entitlements = new StubEntitlements();
        using var picker = new AiModelPickerViewModel(new FixedCatalog(catalog), entitlements);
        bool retainRecovery = true;
        picker.Filter = model => model.Id != recoveryModel;
        picker.KeepOffered = _ => retainRecovery ? [recoveryModel] : [];

        await picker.LoadAsync(
            AiOperations.VideoGeneration,
            recoveryModel,
            preferredSpecified: true,
            CancellationToken.None);
        Assert.That(picker.SelectedModel, Is.EqualTo(recoveryModel));
        Assert.That(picker.OffersNothingUsable.Value, Is.False);

        retainRecovery = false;
        picker.ReconcileRecoveryModels();

        Assert.Multiple(() =>
        {
            Assert.That(picker.Options, Is.Empty);
            Assert.That(picker.SelectedModel, Is.Null);
            Assert.That(picker.HasChoice.Value, Is.False);
            Assert.That(picker.OffersNothingUsable.Value, Is.True);
        });
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
