using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Beutl.Api.Services;
using Beutl.Language;
using Beutl.Logging;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;
using ReactiveUI.Avalonia;

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
/// Which model an operation should run on.
///
/// The list is fetched rather than declared, because an administrator registers
/// it on the server; until it arrives, and whenever it holds fewer than two
/// entries, the dialog shows no picker and the request names no model, which is
/// how it behaved before models could be chosen.
/// </summary>
internal sealed class AiModelPickerViewModel : IDisposable
{
    private readonly ILogger _logger = Log.CreateLogger<AiModelPickerViewModel>();
    private readonly CompositeDisposable _disposables = [];
    private readonly IAiModelCatalogService _catalog;
    private readonly IAiEntitlementService _entitlements;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    // What the list currently on offer was built from. Reloading is how a model
    // an operator changed reaches a screen that is already open, and most
    // reloads find exactly what is already there — rebuilding then would empty
    // the list and move the user's choice for nothing.
    private AiModelCatalog? _loadedCatalog;
    private AiEntitlements? _loadedEntitlements;
    private int _disposed;
    // Which load is the one whose answer still matters. Two can be in the air
    // at once — switching task while a scheduled reload is fetching — and only
    // the newest may say the list has arrived, or a request could go out while
    // the list for the task on screen is still on its way.
    private int _loadGeneration;
    // How often a page that stays where it is asks again. The catalog itself is
    // cached with a freshness window, so most of these cost nothing; without
    // them, a page left in the foreground goes on offering a model an operator
    // has since withdrawn for as long as it is left there. Reopening the page,
    // or coming back to the window, is the other way this happens.
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
        Label = Strings.AiModel;
        Observable
            .Interval(s_reloadInterval, AvaloniaScheduler.Instance)
            .Subscribe(_ => ReloadOnSchedule())
            .DisposeWith(_disposables);
    }

    /// <summary>
    /// Whether the list for <paramref name="operation"/> may be put on offer
    /// right now.
    /// </summary>
    /// <remarks>
    /// A screen holding a name the server has not settled says no to replacing
    /// the list that request was built against: what it carries follows the
    /// model on offer, and moving that renames a request already paid for. It
    /// must not say no to a list it does not have yet, though — a screen with
    /// no list for what it is showing cannot send anything at all, which is how
    /// coming back to an unfinished request would strand it.
    /// </remarks>
    public Func<AiOperationId, bool>? CanReload { get; set; }

    /// <summary>
    /// Models that must stay on the list for an operation however the catalog
    /// changes, because a request still waiting to be collected named them.
    /// </summary>
    /// <remarks>
    /// The server answers such a request before it looks at whether the model
    /// is still offered, so the screen has to be able to say that model's name.
    /// There may be more than one — two requests on the same operation, sent on
    /// different models, can both be waiting.
    /// </remarks>
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

    public string Label { get; }

    public ObservableCollection<AiModelPickerOption> Options { get; } = [];

    /// <summary>
    /// What all the reference pictures of one request may come to together, as
    /// the server publishes it.
    /// </summary>
    public long MaxImageReferencesTotalBytes { get; private set; } =
        AiRequestLimits.MaxImageReferencesTotalBytes;

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
    /// True when the server registered models for this operation and none of
    /// them can serve it.
    /// </summary>
    /// <remarks>
    /// An empty list on its own says nothing — a server that publishes no
    /// models at all is answered by naming none and letting it choose, which is
    /// how this client behaved before models could be chosen. It is only when
    /// there were models to offer and every one was ruled out that a request
    /// would be refused however it is shaped, and the screen has nothing to
    /// offer rather than nothing to choose between.
    /// </remarks>
    public ReactivePropertySlim<bool> OffersNothingUsable { get; }

    /// <summary>
    /// Whether the list has been asked for at least once.
    /// </summary>
    /// <remarks>
    /// Until then nothing is known: the operation may have no model that can
    /// serve it, and a request sent meanwhile names none and runs on whatever
    /// the server has as its default — which may cost more than the model the
    /// screen would have offered. A failed attempt still counts as asked: a
    /// catalog that cannot be read must not leave the screen unable to send at
    /// all, which is how it behaved before models could be chosen.
    /// </remarks>
    public ReactivePropertySlim<bool> IsLoaded { get; }

    /// <summary>What the request should carry. Null asks the server for its default.</summary>
    public AiModelId? SelectedModel => Selected.Value?.Id;

    public Task LoadAsync(AiOperationId operation, CancellationToken cancellationToken)
        => LoadAsync(operation, null, cancellationToken);

    /// <summary>
    /// Loads the list and lands on <paramref name="preferred"/> when it is
    /// still offered.
    /// </summary>
    /// <remarks>
    /// A run that was interrupted names its requests partly by the model it ran
    /// on, so a resumed run has to go back to that model: landing on whichever
    /// the account can afford today would name the unfinished pieces
    /// differently and buy them again.
    /// </remarks>
    public async Task LoadAsync(
        AiOperationId operation,
        AiModelId? preferred,
        CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;

        // Nothing is known about an operation this picker has not been asked
        // for yet — least of all whether it has a model that can serve it. The
        // image editor's five tasks are five operations, and a request sent
        // while the list for the new one is still on its way would name no
        // model and run on the server's default, which may cost more than the
        // model the screen was about to offer.
        if (Operation != operation)
            IsLoaded.Value = false;

        int generation = Interlocked.Increment(ref _loadGeneration);
        try
        {
            await LoadCoreAsync(operation, preferred, generation, cancellationToken);
        }
        finally
        {
            // 頼んだ operation の一覧が実際に手元にあるときだけ「読み込んだ」と
            // 言う。失敗しても言ってしまうと、その task のモデルを 1 つも持たない
            // まま送れてしまい、画面にあるものとは別の——サーバーの既定の——
            // モデルで課金される。
            if (Volatile.Read(ref _disposed) == 0
                && generation == Volatile.Read(ref _loadGeneration))
                IsLoaded.Value = Operation == operation;
        }
    }

    private async Task LoadCoreAsync(
        AiOperationId operation,
        AiModelId? preferred,
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
        if (preferred is null
            && Operation == operation
            && ReferenceEquals(_loadedCatalog, catalog)
            && ReferenceEquals(_loadedEntitlements, entitlements))
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
        AiModelId? keep = preferred ?? SelectedModel;
        // Models the catalog no longer lists that an uncollected request was
        // sent with. The server answers such a request before it looks at
        // whether the model is still offered, so each one stays on the list,
        // unpickable for anything new, until that request is settled. What is on
        // offer right now may be another operation's list entirely, so a name
        // with nothing behind it is shown as itself rather than dropped.
        AiModelPickerOption[] mustStay = (KeepOffered?.Invoke(operation) ?? [])
            .Select(id => Options.FirstOrDefault(option => option.Id == id)
                ?? new AiModelPickerOption(
                    new AiModelOption(id, id.Value, null, false),
                    IsAvailable: false))
            .ToArray();
        Operation = operation;
        MaxImageReferencesTotalBytes = catalog.MaxImageReferencesTotalBytes;
        _loadedCatalog = catalog;
        _loadedEntitlements = entitlements;
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

        HasChoice.Value = Options.Count > 1;
        // Two ways an operation has nothing to run on: every model it registered
        // was ruled out here, or the server named the operation and offered no
        // model for it at all. A server that says nothing about the operation is
        // neither — a request then names no model and the server picks.
        OffersNothingUsable.Value =
            (!registered.IsDefaultOrEmpty && Options.Count == 0)
            || catalog.OffersNoModel(operation);
        // Start on the first model the account can actually pay for, falling
        // back to the server's default so the picker is never empty.
        Selected.Value = (keep is { } wanted
                             ? Options.FirstOrDefault(option => option.Id == wanted)
                             : null)
                         ?? Options.FirstOrDefault(option => option.IsAvailable)
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
