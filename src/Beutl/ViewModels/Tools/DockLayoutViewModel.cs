using System.Reactive.Disposables;
using System.Text.Json.Nodes;
using Beutl.Logging;
using Beutl.Services;
using Beutl.Services.PrimitiveImpls;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.ViewModels.Tools;

/// <summary>
/// Drives the dock layout tool tab. Names are collected by the view through a flyout, so the
/// operations take the target and the name as arguments instead of exposing text-box state.
/// </summary>
public sealed class DockLayoutViewModel : IToolContext
{
    private readonly ILogger _logger = Log.CreateLogger<DockLayoutViewModel>();
    private readonly CompositeDisposable _disposables = [];
    private readonly EditViewModel _editViewModel;
    private readonly DockLayoutPresetService _service;

    public DockLayoutViewModel(EditViewModel editViewModel)
        : this(editViewModel, DockLayoutPresetService.Instance)
    {
    }

    internal DockLayoutViewModel(EditViewModel editViewModel, DockLayoutPresetService service)
    {
        _editViewModel = editViewModel;
        _service = service;

        SelectedItem = new ReactiveProperty<DockLayoutPresetItem?>().AddTo(_disposables);
    }

    public ToolTabExtension Extension => DockLayoutTabExtension.Instance;

    public IReactiveProperty<bool> IsSelected { get; } = new ReactiveProperty<bool>();

    public IReadOnlyReactiveProperty<string> Header { get; } = new ReactivePropertySlim<string>(Strings.DockLayout);

    public ICoreList<DockLayoutPresetItem> Items => _service.Items;

    public ReactiveProperty<DockLayoutPresetItem?> SelectedItem { get; }

    public string SuggestName()
    {
        string baseName = Strings.DockLayout;
        if (!_service.Exists(baseName)) return baseName;

        for (int i = 2; i < int.MaxValue; i++)
        {
            string candidate = $"{baseName} {i}";
            if (!_service.Exists(candidate)) return candidate;
        }

        return baseName;
    }

    public DockLayoutPresetItem? Save(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        DockLayoutPresetItem? saved = _service.Save(name, _editViewModel.DockHost.CaptureLayout());
        if (saved is null)
        {
            NotificationService.ShowError(Strings.DockLayout, MessageStrings.OperationFailed);
            return null;
        }

        SelectedItem.Value = saved;
        NotificationService.ShowSuccess(
            Strings.DockLayout,
            string.Format(MessageStrings.DockLayoutSaved, saved.Name.Value));
        return saved;
    }

    /// <summary>Applies <paramref name="item"/>.</summary>
    public bool Apply(DockLayoutPresetItem item)
    {
        if (_editViewModel.DockHost.ApplyLayout(item.Layout))
        {
            _logger.LogInformation("Applied dock layout '{Name}'.", item.Name.Value);
            return true;
        }

        _logger.LogWarning("Failed to apply dock layout '{Name}'.", item.Name.Value);
        NotificationService.ShowError(Strings.DockLayout, MessageStrings.OperationFailed);
        return false;
    }

    public void Remove(DockLayoutPresetItem item)
    {
        if (!_service.Remove(item))
        {
            NotificationService.ShowError(Strings.DockLayout, MessageStrings.OperationFailed);
            return;
        }

        if (ReferenceEquals(SelectedItem.Value, item))
        {
            SelectedItem.Value = null;
        }
    }

    public bool Rename(DockLayoutPresetItem item, string? newName)
    {
        if (string.IsNullOrWhiteSpace(newName)) return false;

        // Dismissing the flyout unedited must not report an error.
        if (string.Equals(item.Name.Value, newName.Trim(), StringComparison.Ordinal)) return true;

        if (_service.Rename(item, newName)) return true;

        NotificationService.ShowError(Strings.DockLayout, MessageStrings.OperationFailed);
        return false;
    }

    public void ResetLayout()
    {
        _editViewModel.DockHost.ResetLayout();
    }

    public void Dispose()
    {
        _disposables.Dispose();
    }

    public object? GetService(Type serviceType)
    {
        return _editViewModel.GetService(serviceType);
    }

    public void ReadFromJson(JsonObject json)
    {
        // Saved layouts live in BEUTL_HOME, not in the per-scene view state.
    }

    public void WriteToJson(JsonObject json)
    {
    }
}
