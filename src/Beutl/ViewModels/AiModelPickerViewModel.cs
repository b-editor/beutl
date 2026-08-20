using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Reactive.Disposables;
using Beutl.Api.Services;
using Beutl.Language;
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
/// Which model an operation should run on.
///
/// The list is fetched rather than declared, because an administrator registers
/// it on the server; until it arrives, and whenever it holds fewer than two
/// entries, the dialog shows no picker and the request names no model, which is
/// how it behaved before models could be chosen.
/// </summary>
internal sealed class AiModelPickerViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = [];
    private readonly IAiModelCatalogService _catalog;
    private readonly IAiEntitlementService _entitlements;
    // What the list currently on offer was built from. Reloading is how a model
    // an operator changed reaches a screen that is already open, and most
    // reloads find exactly what is already there — rebuilding then would empty
    // the list and move the user's choice for nothing.
    private AiModelCatalog? _loadedCatalog;
    private AiEntitlements? _loadedEntitlements;

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
        Label = Strings.AiModel;
    }

    /// <summary>
    /// What the last load was for. The image editor's five tasks are five
    /// operations with five model lists, so this changes as the task does.
    /// </summary>
    public AiOperationId Operation { get; private set; }

    public string Label { get; }

    public ObservableCollection<AiModelPickerOption> Options { get; } = [];

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
        AiModelCatalog catalog = await _catalog.GetAsync(cancellationToken);
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
        Operation = operation;
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

        HasChoice.Value = Options.Count > 1;
        OffersNothingUsable.Value = !registered.IsDefaultOrEmpty && Options.Count == 0;
        // Start on the first model the account can actually pay for, falling
        // back to the server's default so the picker is never empty.
        Selected.Value = (keep is { } wanted
                             ? Options.FirstOrDefault(option => option.Id == wanted)
                             : null)
                         ?? Options.FirstOrDefault(option => option.IsAvailable)
                         ?? Options.FirstOrDefault();
    }

    public void Dispose() => _disposables.Dispose();
}
