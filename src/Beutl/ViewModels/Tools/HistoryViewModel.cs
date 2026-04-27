using System.Reactive.Linq;
using System.Text.Json.Nodes;
using Avalonia.Threading;
using Beutl.Editor;
using Beutl.Logging;
using Beutl.Services.PrimitiveImpls;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.ViewModels.Tools;

public sealed class HistoryViewModel : IToolContext
{
    private readonly ILogger _logger = Log.CreateLogger<HistoryViewModel>();
    private readonly CompositeDisposable _disposables = [];
    private readonly EditViewModel _editViewModel;
    private readonly HistoryManager _historyManager;

    public HistoryViewModel(EditViewModel editViewModel)
    {
        _editViewModel = editViewModel;
        _historyManager = editViewModel.HistoryManager;

        Entries = _historyManager.Entries;
        CurrentIndex = new ReactivePropertySlim<int>(_historyManager.CurrentIndex);

        _historyManager.StateChanged
            .Subscribe(_ =>
            {
                if (Dispatcher.UIThread.CheckAccess())
                {
                    CurrentIndex.Value = _historyManager.CurrentIndex;
                }
                else
                {
                    Dispatcher.UIThread.Post(() => CurrentIndex.Value = _historyManager.CurrentIndex);
                }
            })
            .DisposeWith(_disposables);
    }

    public ToolTabExtension Extension => HistoryTabExtension.Instance;

    public IReactiveProperty<bool> IsSelected { get; } = new ReactiveProperty<bool>();

    public string Header => Strings.History;

    public IReadOnlyList<HistoryEntry> Entries { get; }

    public ReactivePropertySlim<int> CurrentIndex { get; }

    public void JumpTo(HistoryEntry entry)
    {
        if (entry is null) return;
        var index = ((System.Collections.Generic.IList<HistoryEntry>)Entries).IndexOf(entry);
        if (index < 0) return;

        try
        {
            _historyManager.JumpTo(index);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to jump to history index {Index}", index);
        }
    }

    public void Undo()
    {
        try
        {
            _historyManager.Undo();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to undo");
        }
    }

    public void Redo()
    {
        try
        {
            _historyManager.Redo();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to redo");
        }
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
    }

    public void WriteToJson(JsonObject json)
    {
    }
}
