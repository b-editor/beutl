using System.Collections.ObjectModel;
using Avalonia.Threading;
using Beutl.Services;
using Beutl.ViewModels.ExtensionsPages;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.ViewModels;

public sealed class CommandPaletteViewModel : BaseViewModel
{
    private static readonly TimeSpan s_queryThrottle = TimeSpan.FromMilliseconds(80);
    private static readonly TimeSpan s_stateChangeThrottle = TimeSpan.FromMilliseconds(50);

    private readonly CommandPaletteService _service;
    private readonly ObservableCollection<CommandPaletteItemViewModel> _filteredCommands = [];
    private IReadOnlyList<PaletteCommand> _snapshot = [];
    private readonly IDisposable _querySubscription;
    private readonly IDisposable _activeTabSubscription;
    private CompositeDisposable _stateChangeSubscriptions = [];

    public CommandPaletteViewModel(CommandPaletteService service)
    {
        _service = service;
        FilteredCommands = new ReadOnlyObservableCollection<CommandPaletteItemViewModel>(_filteredCommands);

        _querySubscription = Query
            .Skip(1)
            .Throttle(s_queryThrottle)
            .ObserveOnUIDispatcher()
            .Subscribe(_ => RefreshFiltered());

        // パレット表示中にアクティブタブが切り替わったらスナップショットを取り直して
        // コマンド一覧と CanExecute 結果を最新の状態に追従させる。
        _activeTabSubscription = EditorService.Current.SelectedTabItem
            .Skip(1)
            .ObserveOnUIDispatcher()
            .Subscribe(_ =>
            {
                if (IsOpen.Value)
                {
                    RebuildSnapshot();
                }
            });
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
        Query.Value = string.Empty;
        RebuildSnapshot();
        IsOpen.Value = true;
    }

    private void RebuildSnapshot()
    {
        // _snapshot / _stateChangeSubscriptions / _filteredCommands は同期化していないため、
        // UI スレッドからのみ呼び出されることを明示的にチェックする。
        Dispatcher.UIThread.VerifyAccess();

        _snapshot = _service.EnumerateCommands();

        // 旧購読を Dispose する前に新購読を確立し、Clear→Subscribe の隙間で通知が落ちる窓を排除する。
        // 重複期間は Throttle が吸収し、後で旧購読は破棄される。
        var newSubscriptions = new CompositeDisposable();

        // 同じハンドラーから複数のコマンドが来る場合 StateChanged の observable は同一インスタンスになるため
        // Distinct で重複購読を避ける。通知時はバースト抑止のため軽くスロットリングして RefreshFiltered する。
        foreach (IObservable<Unit> observable in _snapshot
            .Select(c => c.StateChanged)
            .OfType<IObservable<Unit>>()
            .Distinct())
        {
            observable
                .Throttle(s_stateChangeThrottle)
                .ObserveOnUIDispatcher()
                .Subscribe(_ =>
                {
                    if (IsOpen.Value)
                    {
                        RefreshFiltered();
                    }
                })
                .AddTo(newSubscriptions);
        }

        CompositeDisposable previous = _stateChangeSubscriptions;
        _stateChangeSubscriptions = newSubscriptions;
        previous.Dispose();

        // Throttle 経由ではなく即時に最新のスナップショットへ反映する
        RefreshFiltered();
    }

    public void Close()
    {
        IsOpen.Value = false;
        SelectedCommand.Value = null;

        // パレットを閉じた時点で snapshot 内の closure に閉じ込めたハンドラー参照と購読を解放し、
        // 開いていない間にハンドラーや Subject を不必要に保持しないようにする。
        _filteredCommands.Clear();
        _snapshot = [];
        CompositeDisposable previous = _stateChangeSubscriptions;
        _stateChangeSubscriptions = [];
        previous.Dispose();
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

    // CanExecute はパレットを開いた時点とクエリ変更時に評価するほか、
    // RebuildSnapshot で購読した StateChanged 通知 (s_stateChangeThrottle 経由) でも再評価される。
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

        ApplyFilteredCommands(matches);
        HasNoResults.Value = _filteredCommands.Count == 0 && !string.IsNullOrEmpty(query);
    }

    // Clear+Add せず、同じ PaletteCommand インスタンスかつ IsEnabled が同一の項目は再利用して
    // リスト更新時のチラつきを抑える。RebuildSnapshot 後は Id が同じでも別インスタンスの
    // デリゲートを保持する PaletteCommand に差し替わるため、参照同一性で比較して必ず置換する。
    private void ApplyFilteredCommands(List<CommandPaletteItemViewModel> next)
    {
        string? previousSelectedId = SelectedCommand.Value?.Command.Id;

        int common = Math.Min(_filteredCommands.Count, next.Count);
        for (int i = 0; i < common; i++)
        {
            CommandPaletteItemViewModel existing = _filteredCommands[i];
            CommandPaletteItemViewModel candidate = next[i];
            if (!ReferenceEquals(existing.Command, candidate.Command)
                || existing.IsEnabled != candidate.IsEnabled)
            {
                _filteredCommands[i] = candidate;
            }
        }

        while (_filteredCommands.Count > next.Count)
        {
            _filteredCommands.RemoveAt(_filteredCommands.Count - 1);
        }

        for (int i = _filteredCommands.Count; i < next.Count; i++)
        {
            _filteredCommands.Add(next[i]);
        }

        CommandPaletteItemViewModel? preserved = previousSelectedId is null
            ? null
            : _filteredCommands.FirstOrDefault(i => i.Command.Id == previousSelectedId);
        SelectedCommand.Value = preserved ?? _filteredCommands.FirstOrDefault();
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
        _activeTabSubscription.Dispose();
        _stateChangeSubscriptions.Dispose();
        IsOpen.Dispose();
        Query.Dispose();
        SelectedCommand.Dispose();
        HasNoResults.Dispose();
    }
}
