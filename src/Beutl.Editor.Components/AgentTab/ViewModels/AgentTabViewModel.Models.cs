using Beutl.Agents;
using FluentIcons.Common;

namespace Beutl.Editor.Components.AgentTab.ViewModels;

internal sealed partial class AgentTabViewModel
{
    private readonly Dictionary<string, IReadOnlyList<AgentModelDescriptor>> _modelCatalogs = new(StringComparer.Ordinal);
    private readonly HashSet<string> _loadingCatalogs = new(StringComparer.Ordinal);
    private int _catalogRevision;
    private AgentModelOption? _draftModel;
    private string? _pickerProviderId;
    private bool _showSecondaryModels;

    private AgentModelOption EffectiveModel => _selectedThread is { } thread
        ? new(thread.Model, thread.ReasoningEffort)
        : _draftModel ?? new AgentModelOption(_host.DefaultModel, _host.DefaultReasoningEffort);

    public AgentProviderKind ProviderKind => SelectedProvider?.Descriptor.Kind ?? AgentProviderKind.Acp;
    public string ShortModelName => CurrentModel()?.ShortName ?? EffectiveModel.Id ?? ProviderName;
    public string? ReasoningEffortLabel => EffectiveModel.Effort is { } effort ? EffortLabel(effort) : null;
    public string ModelButtonLabel => string.Join(" ", new[] { ShortModelName, ReasoningEffortLabel }.OfType<string>());
    public bool ShowMoreModels { get; private set => Set(ref field, value); }
    public IReadOnlyList<AgentModelProviderRow> ModelProviders { get; private set => Set(ref field, value); } = [];
    public IReadOnlyList<AgentModelRow> ModelRows { get; private set => Set(ref field, value); } = [];
    public AgentModelRow? SelectedModelRow { get; private set => Set(ref field, value); }
    public IReadOnlyList<AgentEffortRow> ModelEfforts { get; private set => Set(ref field, value); } = [];
    public AgentEffortRow? SelectedEffortRow { get; private set => Set(ref field, value); }

    public string? ModelSearch
    {
        get;
        set
        {
            if (Set(ref field, value))
                RefreshModelRows();
        }
    }

    internal Task OpenModelPickerAsync()
    {
        _pickerProviderId = SelectedProvider?.Descriptor.Id;
        _showSecondaryModels = false;
        ModelSearch = null;
        RefreshModelSelection();
        return LoadCatalogAsync(SelectedProvider, refresh: ProviderKind == AgentProviderKind.Acp);
    }

    internal Task BrowseModelsAsync(AgentProviderChoice provider)
    {
        _pickerProviderId = provider.Descriptor.Id;
        _showSecondaryModels = false;
        RefreshModelSelection();
        return LoadCatalogAsync(provider);
    }

    internal void ExpandModelList()
    {
        _showSecondaryModels = true;
        RefreshModelRows();
    }

    internal async Task ChooseModelAsync(AgentModelRow row)
    {
        if (!CanChooseModel || !row.CanSelect)
            return;

        string? modelId = row.Model.Id.Length == 0 ? null : row.Model.Id;
        if (row.IsSelected)
            return;

        if (_selectedThread is null)
        {
            _draftProviderId = row.Provider.Descriptor.Id;
            _draftModel = new AgentModelOption(modelId, null);
            UpdateProviderSelection();
        }
        else if (row.Provider.Descriptor.Id == _selectedThread.ProviderId)
        {
            await SaveThreadModelAsync(modelId, null);
        }
        else
        {
            await ConfigureThreadAsync((controller, ct) => controller.SetProviderAsync(row.Provider.Descriptor, modelId, ct));
        }
    }

    internal async Task ChooseEffortAsync(AgentEffortRow row)
    {
        if (!CanChooseModel || row.IsSelected)
            return;
        var model = EffectiveModel;
        if (_selectedThread is null)
        {
            _draftModel = model with { Effort = row.Value };
            RefreshModelSelection();
        }
        else
        {
            await SaveThreadModelAsync(model.Id, row.Value);
        }
    }

    private Task SaveThreadModelAsync(string? model, string? effort)
        => ConfigureThreadAsync((controller, ct) => controller.SetModelAsync(model, effort, ct));

    private void InvalidateModelCatalogs()
    {
        _catalogRevision++;
        _modelCatalogs.Clear();
        _loadingCatalogs.Clear();
    }

    private async Task LoadCatalogAsync(AgentProviderChoice? provider, bool refresh = false)
    {
        if (_disposed || provider is not { IsAvailable: true })
            return;
        string id = provider.Descriptor.Id;
        if ((!refresh && _modelCatalogs.ContainsKey(id)) || !_loadingCatalogs.Add(id))
            return;

        var ct = _lifetime.Token;
        int revision = _catalogRevision;
        try
        {
            Guid? threadId = _selectedThread?.ProviderId == id ? _selectedThread.Id : null;
            var catalog = await _host.Runtime.ListModelsAsync(_projectPath, provider.Descriptor, threadId, ct);
            if (!ct.IsCancellationRequested && revision == _catalogRevision)
            {
                _modelCatalogs[id] = catalog;
                RefreshModelSelection();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        finally
        {
            if (revision == _catalogRevision)
                _loadingCatalogs.Remove(id);
        }
    }

    private IReadOnlyList<AgentModelDescriptor> Catalog(AgentProviderChoice provider)
        => _modelCatalogs.TryGetValue(provider.Descriptor.Id, out var catalog) ? catalog : AgentModelCatalog.GetFallback(provider.Descriptor);

    private AgentModelDescriptor? CurrentModel()
    {
        if (SelectedProvider is not { } provider)
            return null;
        return FindModel(Catalog(provider), EffectiveModel.Id);
    }

    private static AgentModelDescriptor? FindModel(IReadOnlyList<AgentModelDescriptor> models, string? id)
        => id is null ? models.FirstOrDefault(model => model.IsProviderDefault)
            : models.FirstOrDefault(model => model.Id == id) ?? models.FirstOrDefault(model => model.ResolvedModelId == id);

    private AgentTimelineDividerItem BindModelChange(AgentThreadModelChanged change)
    {
        string oldEffort = change.PreviousEffort is { } previous ? EffortLabel(previous) : AgentStrings.EffortDefault;
        string newEffort = change.Effort is { } effort ? EffortLabel(effort) : AgentStrings.EffortDefault;
        string format = AgentStrings.EffortChangeNotice;
        string from = oldEffort;
        string to = newEffort;
        if (change.PreviousModel != change.Model)
        {
            var provider = _detectedProviders.FirstOrDefault(item => item.Descriptor.Id == change.ProviderId);
            var catalog = provider is not null ? Catalog(provider) : [];
            from = FindModel(catalog, change.PreviousModel)?.ShortName ?? change.PreviousModel ?? AgentStrings.EffortDefault;
            to = FindModel(catalog, change.Model)?.ShortName ?? change.Model ?? AgentStrings.EffortDefault;
            if (change.PreviousEffort != change.Effort)
            {
                from = $"{from} / {oldEffort}";
                to = $"{to} / {newEffort}";
            }
            format = AgentStrings.ModelChangeNotice;
        }
        return new AgentTimelineDividerItem(change.TurnId, string.Format(CultureInfo.CurrentCulture, format, from, to), Icon.ArrowSwap);
    }

    private void RefreshModelSelection()
    {
        Notify(nameof(ProviderKind));
        Notify(nameof(ShortModelName));
        Notify(nameof(ReasoningEffortLabel));
        Notify(nameof(ModelButtonLabel));
        NotifyContextMeter();

        string? activeProviderId = _pickerProviderId ?? SelectedProvider?.Descriptor.Id;
        ModelProviders = Providers.OrderBy(provider => provider.Descriptor.Kind switch
        {
            AgentProviderKind.Codex => 0,
            AgentProviderKind.ClaudeCode => 1,
            _ => 2
        }).Select(provider => new AgentModelProviderRow(provider, provider.Descriptor.Id == activeProviderId)).ToArray();

        string? effort = EffectiveModel.Effort;
        var model = CurrentModel();
        string? preferred = model?.DefaultReasoningEffort;
        var levels = model?.ReasoningEfforts ?? [];
        var rows = levels.Where(level => !string.IsNullOrWhiteSpace(level)).Distinct(StringComparer.Ordinal)
            .Select(level => new AgentEffortRow(level == preferred ? null : level, EffortLabel(level),
                level == preferred, level == (effort ?? preferred))).ToList();
        if (rows.Count > 0 && !rows.Any(row => row.IsDefault))
            rows.Insert(0, new AgentEffortRow(null, AgentStrings.EffortDefault, true, effort is null));
        ModelEfforts = rows;
        SelectedEffortRow = rows.FirstOrDefault(row => row.IsSelected);
        RefreshModelRows();
    }

    private void RefreshModelRows()
    {
        string search = ModelSearch?.Trim() ?? "";
        var candidates = new List<(AgentProviderChoice Provider, AgentModelDescriptor Model)>();
        var provider = Providers.FirstOrDefault(item => item.Descriptor.Id == _pickerProviderId) ?? SelectedProvider;

        if (provider is not null)
        {
            candidates.AddRange(Catalog(provider)
                .Where(model => model.Id != "default" || !model.IsProviderDefault)
                .Select(model => (provider, model)));
            candidates.Add((provider, new AgentModelDescriptor("", AgentStrings.ModelProviderDefault, "", provider.Descriptor.Id, [],
                IsSecondary: candidates.Count >= 4)));
        }

        var matched = candidates.Where(candidate => search.Length == 0 || MatchesSearch(candidate.Model, search)).ToArray();
        ShowMoreModels = provider is not null && search.Length == 0 && !_showSecondaryModels && matched.Any(row => row.Model.IsSecondary);
        var visible = matched.Where(candidate => search.Length > 0 || _showSecondaryModels || !candidate.Model.IsSecondary);
        var rows = visible.Select(candidate => CreateModelRow(candidate.Provider, candidate.Model)).ToList();
        if (rows.Count == 0 && search.Length > 0 && provider is not null)
        {
            var model = new AgentModelDescriptor(search, string.Format(AgentStrings.UseCustomModel, search), search,
                provider.Descriptor.Id, []);
            rows.Add(CreateModelRow(provider, model));
        }
        ModelRows = rows;
        SelectedModelRow = rows.FirstOrDefault(row => row.IsSelected);
    }

    private AgentModelRow CreateModelRow(AgentProviderChoice provider, AgentModelDescriptor model)
    {
        string? modelId = EffectiveModel.Id;
        bool isSelected = provider.Descriptor.Id == SelectedProvider?.Descriptor.Id
            && (model.Id == (modelId ?? "") || (modelId is not null && modelId == model.ResolvedModelId));
        return new AgentModelRow(provider, model, isSelected, provider.IsAvailable);
    }

    private static bool MatchesSearch(AgentModelDescriptor model, string search) =>
        model.Id.Contains(search, StringComparison.OrdinalIgnoreCase)
        || model.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase)
        || model.ShortName.Contains(search, StringComparison.OrdinalIgnoreCase)
        || model.ResolvedModelId?.Contains(search, StringComparison.OrdinalIgnoreCase) == true;

    private static string EffortLabel(string level) => level switch
    {
        "none" => AgentStrings.EffortNone,
        "minimal" => AgentStrings.EffortMinimal,
        "low" => AgentStrings.EffortLow,
        "medium" => AgentStrings.EffortMedium,
        "high" => AgentStrings.EffortHigh,
        "xhigh" => AgentStrings.EffortExtraHigh,
        "max" => AgentStrings.EffortMax,
        "ultra" => AgentStrings.EffortUltra,
        _ => level
    };
}

internal sealed record AgentModelProviderRow(AgentProviderChoice Provider, bool IsSelected)
{
    public AgentProviderKind Kind => Provider.Descriptor.Kind;
}

internal sealed record AgentModelRow(AgentProviderChoice Provider, AgentModelDescriptor Model,
    bool IsSelected, bool CanSelect)
{
    public AgentProviderKind Kind => Provider.Descriptor.Kind;
}

internal sealed record AgentEffortRow(string? Value, string Label, bool IsDefault, bool IsSelected)
{
    public bool ShowDefaultBadge => IsDefault && Label != AgentStrings.EffortDefault;
}
