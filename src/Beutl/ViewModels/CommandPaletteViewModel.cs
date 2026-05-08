using System.Collections.ObjectModel;
using Beutl.Services;
using Beutl.ViewModels.ExtensionsPages;
using Reactive.Bindings;

namespace Beutl.ViewModels;

public sealed class CommandPaletteViewModel : BaseViewModel
{
    private readonly CommandPaletteService _service;
    private readonly ObservableCollection<CommandPaletteItemViewModel> _filteredCommands = [];
    private IReadOnlyList<PaletteCommand> _snapshot = [];
    private readonly IDisposable _querySubscription;

    public CommandPaletteViewModel(CommandPaletteService service)
    {
        _service = service;
        FilteredCommands = new ReadOnlyObservableCollection<CommandPaletteItemViewModel>(_filteredCommands);

        _querySubscription = Query.Subscribe(_ => RefreshFiltered());
    }

    public ReactivePropertySlim<bool> IsOpen { get; } = new(false);

    public ReactivePropertySlim<string> Query { get; } = new(string.Empty);

    public ReadOnlyObservableCollection<CommandPaletteItemViewModel> FilteredCommands { get; }

    public ReactivePropertySlim<CommandPaletteItemViewModel?> SelectedCommand { get; } = new();

    public ReactivePropertySlim<bool> HasNoResults { get; } = new(false);

    public void Toggle()
    {
        if (IsOpen.Value)
        {
            Close();
        }
        else
        {
            Open();
        }
    }

    public void Open()
    {
        _snapshot = _service.EnumerateCommands();
        if (Query.Value.Length == 0)
        {
            RefreshFiltered();
        }
        else
        {
            // Query.Value への代入で _querySubscription 経由で RefreshFiltered が走る
            Query.Value = string.Empty;
        }

        IsOpen.Value = true;
    }

    public void Close()
    {
        IsOpen.Value = false;
        SelectedCommand.Value = null;
    }

    public void ExecuteSelected()
    {
        CommandPaletteItemViewModel? item = SelectedCommand.Value;
        if (item is { IsEnabled: true })
        {
            Close();
            item.Execute();
        }
    }

    public void MoveSelection(int delta)
    {
        if (_filteredCommands.Count == 0)
        {
            SelectedCommand.Value = null;
            return;
        }

        int currentIndex = SelectedCommand.Value is { } current
            ? _filteredCommands.IndexOf(current)
            : -1;
        int next = currentIndex + delta;
        if (next < 0) next = 0;
        if (next >= _filteredCommands.Count) next = _filteredCommands.Count - 1;
        SelectedCommand.Value = _filteredCommands[next];
    }

    public void SelectFirst()
    {
        SelectedCommand.Value = _filteredCommands.FirstOrDefault();
    }

    public void SelectLast()
    {
        SelectedCommand.Value = _filteredCommands.LastOrDefault();
    }

    // CanExecute はパレットを開いた時点とクエリ変更時のスナップショットで評価する。
    // 表示中に外部の状態が変化しても再評価しないが、コマンドパレットは短命 UI のため許容している。
    private void RefreshFiltered()
    {
        string query = Query.Value?.Trim() ?? string.Empty;

        var matches = new List<CommandPaletteItemViewModel>();
        foreach (PaletteCommand command in _snapshot)
        {
            int relevance = ScoreMatch(command, query);
            if (relevance < 0)
            {
                continue;
            }

            bool isEnabled;
            try
            {
                isEnabled = command.CanExecute();
            }
            catch
            {
                isEnabled = false;
            }

            matches.Add(new CommandPaletteItemViewModel(command, isEnabled, relevance));
        }

        matches.Sort((a, b) =>
        {
            int byRelevance = b.Relevance.CompareTo(a.Relevance);
            if (byRelevance != 0) return byRelevance;
            int byEnabled = b.IsEnabled.CompareTo(a.IsEnabled);
            if (byEnabled != 0) return byEnabled;
            return string.Compare(a.DisplayName, b.DisplayName, StringComparison.CurrentCultureIgnoreCase);
        });

        _filteredCommands.Clear();
        foreach (CommandPaletteItemViewModel item in matches)
        {
            _filteredCommands.Add(item);
        }

        SelectedCommand.Value = _filteredCommands.FirstOrDefault();
        HasNoResults.Value = _filteredCommands.Count == 0 && !string.IsNullOrEmpty(query);
    }

    private static int ScoreMatch(PaletteCommand command, string query)
    {
        if (string.IsNullOrEmpty(query))
        {
            return 0;
        }

        int best = -1;
        Update(ref best, command.DisplayName, query, baseScore: 100);
        Update(ref best, command.Description, query, baseScore: 60);
        Update(ref best, command.CategoryName, query, baseScore: 40);
        return best;
    }

    private static void Update(ref int best, string? haystack, string needle, int baseScore)
    {
        if (string.IsNullOrEmpty(haystack)) return;

        int idx = haystack.IndexOf(needle, StringComparison.CurrentCultureIgnoreCase);
        if (idx < 0) return;

        int score = baseScore;
        if (idx == 0)
        {
            score += 30;
        }
        else if (idx > 0 && !char.IsLetterOrDigit(haystack[idx - 1]))
        {
            score += 15;
        }

        if (score > best) best = score;
    }

    public override void Dispose()
    {
        _querySubscription.Dispose();
        IsOpen.Dispose();
        Query.Dispose();
        SelectedCommand.Dispose();
        HasNoResults.Dispose();
    }
}
