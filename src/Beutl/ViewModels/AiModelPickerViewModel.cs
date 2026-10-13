using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Beutl.Api.Services;
using Beutl.Helpers;
using Beutl.Language;
using Beutl.Logging;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.ViewModels;

/// <summary>
/// One entry in the model picker. It carries no price: the server publishes an
/// ordering (<see cref="AiModelCostTier"/>) and whether the account can pay for
/// this model, and nothing more.
/// </summary>
internal sealed record AiModelPickerOption(AiModelOption Model, bool IsAvailable)
{
    public AiModelId Id => Model.Id;

    public override string ToString()
    {
        string cost = !IsAvailable
            ? Strings.AiModelUnaffordable
            : Model.CostTier switch
            {
                AiModelCostTier.Low => Strings.AiModelCostLow,
                AiModelCostTier.Medium => Strings.AiModelCostMedium,
                AiModelCostTier.High => Strings.AiModelCostHigh,
                _ => string.Empty,
            };
        return cost.Length == 0 ? Model.DisplayName : $"{Model.DisplayName} — {cost}";
    }
}

/// <summary>
/// Which model an operation should run on. The list comes from the server; until it arrives, and whenever it
/// holds fewer than two entries, the dialog shows no picker and the request names no model.
/// </summary>
internal sealed class AiModelPickerViewModel : IDisposable
{
    private readonly ILogger _logger = Log.CreateLogger<AiModelPickerViewModel>();
    private readonly CompositeDisposable _disposables = [];
    private readonly IAiModelCatalogService _catalog;
    private readonly IAiEntitlementService _entitlements;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    // What the list on offer was built from. Most reloads find exactly this, and rebuilding the list then
    // would empty it and move the user's choice for nothing.
    private AiModelCatalog? _loadedCatalog;
    private AiEntitlements? _loadedEntitlements;
    private int _disposed;
    // The newest load, the only one that may say the list has arrived: a task switch during a scheduled
    // reload puts two in the air, and the older one could let a request out before the right list arrives.
    private int _loadGeneration;
    // How often an open page asks again, so it stops offering a model an operator has withdrawn. The
    // catalog is cached with a freshness window, so most of these cost nothing.
    private static readonly TimeSpan s_reloadInterval = TimeSpan.FromMinutes(1);

    public AiModelPickerViewModel(
        IAiModelCatalogService catalog,
        IAiEntitlementService entitlements)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _entitlements = entitlements ?? throw new ArgumentNullException(nameof(entitlements));

        Selected = new ReactivePropertySlim<AiModelPickerOption?>().DisposeWith(_disposables);
        HasChoice = new ReactivePropertySlim<bool>(false).DisposeWith(_disposables);
        OffersNothingUsable = new ReactivePropertySlim<bool>(false)
            .DisposeWith(_disposables);
        IsLoaded = new ReactivePropertySlim<bool>(false)
            .DisposeWith(_disposables);
        IsSelectionEnabled = new ReactivePropertySlim<bool>(true)
            .DisposeWith(_disposables);
        Observable
            .Interval(s_reloadInterval, UiThreadScheduler.Instance)
            .Subscribe(_ => ReloadOnSchedule())
            .DisposeWith(_disposables);
    }

    /// <summary>
    /// Whether the list for <paramref name="operation"/> may be put on offer right now. A screen holding an
    /// unsettled request says no, since moving the model renames a request already paid for, but never to a
    /// list it does not have yet, which would leave it unable to send anything.
    /// </summary>
    public Func<AiOperationId, bool>? CanReload { get; set; }

    /// <summary>
    /// Models that stay on the list for an operation however the catalog changes, because a request still
    /// waiting to be collected named them; the server answers such a request even for a withdrawn model.
    /// </summary>
    public Func<AiOperationId, IReadOnlyList<AiModelId>>? KeepOffered { get; set; }

    private void ReloadOnSchedule()
    {
        if (Volatile.Read(ref _disposed) != 0
            || !IsLoaded.Value
            || CanReload?.Invoke(Operation) == false)
            return;
        _ = ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        try
        {
            await LoadAsync(Operation, _lifetimeCancellation.Token);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            // Offering yesterday's list is better than emptying it, so nothing
            // is shown for a reload nobody asked for — but a list that has
            // stopped refreshing is worth knowing about.
            _logger.LogWarning(ex, "Failed to reload the AI model list.");
        }
    }

    /// <summary>
    /// What the last load was for. The image editor's five tasks are five
    /// operations with five model lists, so this changes as the task does.
    /// </summary>
    public AiOperationId Operation { get; private set; }

    public ObservableCollection<AiModelPickerOption> Options { get; } = [];

    /// <summary>
    /// What all the reference pictures of one request may come to together, as
    /// the server publishes it.
    /// </summary>
    public AiImageReferenceLimits ImageReferenceLimits { get; private set; }

    public AiCaptionTranslationLimits CaptionTranslationLimits { get; private set; }

    /// <summary>
    /// Which models an operation is willing to offer, beyond what the server
    /// registered. Video drops the ones that take no shape it can ask for:
    /// offering one would only ever produce a request the server refuses.
    /// </summary>
    public Func<AiModelOption, bool>? Filter { get; set; }

    public ReactivePropertySlim<AiModelPickerOption?> Selected { get; }

    /// <summary>False while a single model is on offer, which is nothing to choose between.</summary>
    public ReactivePropertySlim<bool> HasChoice { get; }

    /// <summary>
    /// True when the server registered models for this operation and none of them can serve it. An empty list
    /// alone says nothing: a server that publishes no models is answered by naming none and letting it choose.
    /// </summary>
    public ReactivePropertySlim<bool> OffersNothingUsable { get; }

    /// <summary>
    /// Whether the list has been asked for at least once. Until then a request names no model and runs on the
    /// server's default, which may cost more than the model the screen would have offered.
    /// </summary>
    public ReactivePropertySlim<bool> IsLoaded { get; }

    internal ReactivePropertySlim<bool> IsSelectionEnabled { get; }

    /// <summary>What the request should carry. Null asks the server for its default.</summary>
    public AiModelId? SelectedModel => Selected.Value?.Id;

    /// <summary>
    /// Drops models retained only for recovery once their durable request is
    /// gone, and moves the selection back to a model in the current catalog.
    /// </summary>
    internal void ReconcileRecoveryModels()
    {
        if (_loadedCatalog is not { } catalog)
            return;

        ImmutableArray<AiModelOption> registered = catalog.ModelsFor(Operation);
        HashSet<AiModelId> offeredIds = registered
            .Where(model => Filter is not { } filter || filter(model))
            .Select(model => model.Id)
            .ToHashSet();
        HashSet<AiModelId> retainedIds = (KeepOffered?.Invoke(Operation) ?? [])
            .ToHashSet();
        for (int index = Options.Count - 1; index >= 0; index--)
        {
            AiModelId id = Options[index].Id;
            if (!offeredIds.Contains(id) && !retainedIds.Contains(id))
                Options.RemoveAt(index);
        }

        PublishOptionState(registered, catalog, Operation);
        if (Selected.Value is { } selected
            && offeredIds.Contains(selected.Id)
            && Options.Any(option => option.Id == selected.Id))
        {
            return;
        }

        Selected.Value = Options.FirstOrDefault(option =>
                offeredIds.Contains(option.Id) && option.IsAvailable)
            ?? Options.FirstOrDefault(option => offeredIds.Contains(option.Id));
    }

    public Task LoadAsync(AiOperationId operation, CancellationToken cancellationToken)
        => LoadAsync(operation, null, false, cancellationToken);

    public Task LoadAsync(
        AiOperationId operation,
        AiModelId? preferred,
        CancellationToken cancellationToken)
        => LoadAsync(operation, preferred, preferredSpecified: preferred is not null, cancellationToken);

    /// <summary>
    /// Loads the list and lands on <paramref name="preferred"/> when it is still offered, so a resumed run goes
    /// back to the model its requests are named by instead of buying the unfinished pieces again.
    /// </summary>
    public async Task LoadAsync(
        AiOperationId operation,
        AiModelId? preferred,
        bool preferredSpecified,
        CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;

        // Another operation is not loaded until its own list arrives (see IsLoaded).
        if (Operation != operation)
            IsLoaded.Value = false;

        int generation = Interlocked.Increment(ref _loadGeneration);
        try
        {
            await LoadCoreAsync(operation, preferred, preferredSpecified, generation, cancellationToken);
        }
        finally
        {
            // Report the operation as loaded only when its catalog is actually present. Marking a
            // failed load complete would allow a task with no listed model to be sent and charged
            // against the server default instead of the model shown by the UI.
            if (Volatile.Read(ref _disposed) == 0
                && generation == Volatile.Read(ref _loadGeneration))
                IsLoaded.Value = Operation == operation;
        }
    }

    private async Task LoadCoreAsync(
        AiOperationId operation,
        AiModelId? preferred,
        bool preferredSpecified,
        int generation,
        CancellationToken cancellationToken)
    {
        AiModelCatalog catalog = await _catalog.GetAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (Volatile.Read(ref _disposed) != 0)
            return;
        // Overtaken while this was being fetched: the screen has moved on to
        // another operation, and putting this list up would replace the one it
        // is actually showing.
        if (generation != _loadGeneration)
            return;

        // Asked again on the way back. A request may have gone out while this
        // was being fetched, and what it carries — the model, and the shape and
        // background that follow it — is what this list would replace.
        if (CanReload?.Invoke(operation) == false
            && Operation == operation
            && IsLoaded.Value)
            return;

        AiEntitlements? entitlements = _entitlements.Entitlements.Value;
        if (IsUnchangedReload(operation, preferred, preferredSpecified, catalog, entitlements))
        {
            return;
        }

        // Only a reported refusal rules the operation out. Silence says nothing,
        // so its models stay offered rather than reading as unaffordable.
        bool operationIsAvailable =
            entitlements is not null
            && entitlements.Availability.GetState(operation) != AiOperationAvailabilityState.Unavailable;

        // The choice already made, kept when it is still on offer: a reload is
        // not a reason to move it.
        AiModelId? keep = preferredSpecified ? preferred : (preferred ?? SelectedModel);
        // Kept for uncollected requests (see KeepOffered), unpickable; one not on the list shows its id.
        AiModelPickerOption[] mustStay = CollectRetainedOptions(operation);
        Operation = operation;
        ImageReferenceLimits = catalog.GetImageReferenceLimits(operation);
        CaptionTranslationLimits = catalog.CaptionTranslationLimits;
        _loadedCatalog = catalog;
        _loadedEntitlements = entitlements;
        ImmutableArray<AiModelOption> registered =
            PopulateOptions(catalog, operation, entitlements, operationIsAvailable, mustStay);

        PublishOptionState(registered, catalog, operation);
        Selected.Value = ChooseInitialSelection(keep, preferred, preferredSpecified);
    }

    private bool IsUnchangedReload(
        AiOperationId operation,
        AiModelId? preferred,
        bool preferredSpecified,
        AiModelCatalog catalog,
        AiEntitlements? entitlements)
    {
        return !preferredSpecified
            && preferred is null
            && Operation == operation
            && ReferenceEquals(_loadedCatalog, catalog)
            && ReferenceEquals(_loadedEntitlements, entitlements);
    }

    private AiModelPickerOption[] CollectRetainedOptions(AiOperationId operation)
    {
        return (KeepOffered?.Invoke(operation) ?? [])
            .Select(id => Options.FirstOrDefault(option => option.Id == id)
                ?? new AiModelPickerOption(
                    new AiModelOption(id, id.Value, null, false),
                    IsAvailable: false))
            .ToArray();
    }

    private ImmutableArray<AiModelOption> PopulateOptions(
        AiModelCatalog catalog,
        AiOperationId operation,
        AiEntitlements? entitlements,
        bool operationIsAvailable,
        AiModelPickerOption[] mustStay)
    {
        Options.Clear();
        ImmutableArray<AiModelOption> registered = catalog.ModelsFor(operation);
        foreach (AiModelOption model in registered)
        {
            if (Filter is { } filter && !filter(model))
                continue;

            Options.Add(new AiModelPickerOption(
                model,
                entitlements?.ModelAvailability.CanStart(
                    operation,
                    model.Id,
                    operationIsAvailable) ?? false));
        }

        foreach (AiModelPickerOption held in mustStay)
        {
            if (!Options.Any(option => option.Id == held.Id))
                Options.Add(held with { IsAvailable = false });
        }

        return registered;
    }

    private void PublishOptionState(
        ImmutableArray<AiModelOption> registered, AiModelCatalog catalog, AiOperationId operation)
    {
        HasChoice.Value = Options.Count > 1;
        // Every registered model ruled out, or none offered for the operation (see OffersNothingUsable).
        OffersNothingUsable.Value =
            (!registered.IsDefaultOrEmpty && Options.Count == 0)
            || catalog.OffersNoModel(operation);
    }

    // Start on the first model the account can actually pay for, falling
    // back to the server's default so the picker is never empty.
    private AiModelPickerOption? ChooseInitialSelection(AiModelId? keep, AiModelId? preferred, bool preferredSpecified)
    {
        return preferredSpecified && preferred is null
            ? null
            : (keep is { } wanted
                ? Options.FirstOrDefault(option => option.Id == wanted)
                : null)
              ?? Options.FirstOrDefault(option => option.IsAvailable && option.Model.IsDefault)
              ?? Options.FirstOrDefault(option => option.IsAvailable)
              ?? Options.FirstOrDefault(option => option.Model.IsDefault)
              ?? Options.FirstOrDefault();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        Interlocked.Increment(ref _loadGeneration);
        try
        {
            _lifetimeCancellation.Cancel();
        }
        finally
        {
            _disposables.Dispose();
            _lifetimeCancellation.Dispose();
        }
    }
}
