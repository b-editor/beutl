using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Windows.Input;
using Beutl.Api.Services;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Logging;
using Beutl.Services;
using Beutl.ViewModels.Dock;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.ViewModels;

public sealed record CommandPaletteEntry(
    string Id,
    string DisplayName,
    string Category,
    string? KeyGesture,
    Action Execute,
    Func<bool>? CanExecute = null)
{
    public bool CanExecuteNow => CanExecute?.Invoke() ?? true;
}

public sealed class CommandPaletteViewModel : IDisposable
{
    private const int MaxResults = 10;
    private static readonly ILogger s_logger = Log.CreateLogger<CommandPaletteViewModel>();
    private readonly CompositeDisposable _disposables = [];
    private readonly List<CommandPaletteEntry> _allEntries = [];
    private readonly ObservableCollection<CommandPaletteResult> _results = [];

    public CommandPaletteViewModel(MainViewModel mainViewModel)
    {
        MainViewModel = mainViewModel;
        Results = new ReadOnlyObservableCollection<CommandPaletteResult>(_results);

        BuildEntries();

        SearchQuery.Subscribe(UpdateResults).DisposeWith(_disposables);
        SelectedIndex.Subscribe(ClampIndex).DisposeWith(_disposables);

        UpdateResults(string.Empty);
    }

    public MainViewModel MainViewModel { get; }

    public ReactiveProperty<string> SearchQuery { get; } = new(string.Empty);

    public ReactiveProperty<int> SelectedIndex { get; } = new(0);

    public ReactiveProperty<bool> IsEmpty { get; } = new(true);

    public ReadOnlyObservableCollection<CommandPaletteResult> Results { get; }

    public CommandPaletteEntry? GetSelected()
    {
        int index = SelectedIndex.Value;
        if (index < 0 || index >= _results.Count)
            return null;
        return _results[index].Entry;
    }

    public void Execute(CommandPaletteEntry entry)
    {
        try
        {
            if (!entry.CanExecuteNow)
                return;
            entry.Execute();
        }
        catch (Exception ex)
        {
            s_logger.LogError(ex, "Failed to execute command palette entry: {Id}", entry.Id);
            _ = ex.Handle();
        }
    }

    public void MoveSelection(int delta)
    {
        if (_results.Count == 0)
        {
            SelectedIndex.Value = 0;
            return;
        }

        int next = SelectedIndex.Value + delta;
        if (next < 0) next = _results.Count - 1;
        if (next >= _results.Count) next = 0;
        SelectedIndex.Value = next;
    }

    public void Dispose()
    {
        _disposables.Dispose();
        SearchQuery.Dispose();
        SelectedIndex.Dispose();
        IsEmpty.Dispose();
    }

    private void ClampIndex(int index)
    {
        if (_results.Count == 0)
        {
            if (index != 0)
                SelectedIndex.Value = 0;
            return;
        }

        if (index < 0)
            SelectedIndex.Value = 0;
        else if (index >= _results.Count)
            SelectedIndex.Value = _results.Count - 1;
    }

    private void UpdateResults(string query)
    {
        _results.Clear();
        IEnumerable<CommandPaletteEntry> available = _allEntries.Where(e => e.CanExecuteNow);
        IEnumerable<CommandPaletteResult> source;
        if (string.IsNullOrWhiteSpace(query))
        {
            source = available
                .OrderBy(e => e.Category, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(e => e.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .Take(MaxResults)
                .Select(e => new CommandPaletteResult(e, 0));
        }
        else
        {
            source = available
                .Select(e => new { Entry = e, Score = FuzzyMatcher.Score(query, e.DisplayName, e.Category) })
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Entry.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .Take(MaxResults)
                .Select(x => new CommandPaletteResult(x.Entry, x.Score));
        }

        foreach (CommandPaletteResult r in source)
            _results.Add(r);

        IsEmpty.Value = _results.Count == 0;
        SelectedIndex.Value = 0;
    }

    private void BuildEntries()
    {
        // ContextCommandManager に登録された全コマンド
        ContextCommandManager? cm = MainViewModel.ContextCommandManager;
        if (cm != null)
        {
            OSPlatform pid = GetCurrentPlatform();
            foreach (ContextCommandEntry entry in cm.GetDefinitions())
            {
                // パレット自身を開くコマンドは除外（再帰オープン防止）
                if (entry.ExtensionType == typeof(Services.PrimitiveImpls.MainViewExtension)
                    && entry.Definition.Name == "OpenCommandPalette")
                    continue;

                string? gesture = entry.KeyGestures
                    .FirstOrDefault(g => g.Platform == pid)?.KeyGesture?.ToString();
                string display = entry.Definition.DisplayName ?? entry.Definition.Name;
                string category = ResolveExtensionDisplayName(entry.ExtensionType);
                string id = $"ctx::{entry.ExtensionType.FullName}.{entry.Definition.Name}";

                Type extType = entry.ExtensionType;
                string cmdName = entry.Definition.Name;
                _allEntries.Add(new CommandPaletteEntry(
                    id,
                    display,
                    category,
                    gesture,
                    () => InvokeContextCommand(extType, cmdName),
                    () => CanInvokeContextCommand(extType, cmdName)));
            }
        }

        // MenuBarViewModel の Reactive Commands
        // MainViewExtension の ContextCommand と機能が重複するもの (Save / Undo / Exit など) は
        // ctx エントリ側で表示されるため、ここでは追加しない。
        MenuBarViewModel mb = MainViewModel.MenuBar;
        string fileCat = Strings.CommandPalette_File;
        string sceneCat = Strings.CommandPalette_Scene;
        string viewCat = Strings.CommandPalette_View;

        AddMenuCommand("MenuBar.CloseFile", Strings.Close, fileCat, mb.CloseFile);
        AddMenuCommand("MenuBar.ExportProject", Strings.ExportProject, fileCat, mb.ExportProject);
        AddMenuCommand("MenuBar.ImportProject", Strings.ImportProject, fileCat, mb.ImportProject);

        AddMenuCommand("MenuBar.NewScene", Strings.CreateNewScene, sceneCat, mb.NewScene);
        AddMenuCommand("MenuBar.DeleteLayer", Strings.Delete, sceneCat, mb.DeleteLayer);
        AddMenuCommand("MenuBar.ExcludeLayer", Strings.Exclude, sceneCat, mb.ExcludeLayer);
        AddMenuCommand("MenuBar.CutLayer", Strings.Cut, sceneCat, mb.CutLayer);
        AddMenuCommand("MenuBar.CopyLayer", Strings.Copy, sceneCat, mb.CopyLayer);
        AddMenuCommand("MenuBar.PasteLayer", Strings.Paste, sceneCat, mb.PasteLayer);
        AddMenuCommand("MenuBar.ShowSceneSettings", Strings.SceneSettings, sceneCat, mb.ShowSceneSettings);

        AddMenuCommand("MenuBar.ResetDockLayout", Strings.ResetDockLayout, viewCat, mb.ResetDockLayout);

        // 重複削除（同じ Id）。先勝ち。
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var deduped = new List<CommandPaletteEntry>(_allEntries.Count);
        foreach (CommandPaletteEntry entry in _allEntries)
        {
            if (seen.Add(entry.Id))
                deduped.Add(entry);
        }

        _allEntries.Clear();
        _allEntries.AddRange(deduped);
    }

    private void AddMenuCommand(string id, string displayName, string category, ICommand? command, object? parameter = null)
    {
        if (command == null) return;
        _allEntries.Add(new CommandPaletteEntry(
            id,
            displayName,
            category,
            null,
            () =>
            {
                if (command.CanExecute(parameter))
                    command.Execute(parameter);
            },
            () => command.CanExecute(parameter)));
    }

    private void InvokeContextCommand(Type extensionType, string commandName)
    {
        // MainView 自身がハンドラの場合は MainViewModel.Execute を呼ぶ
        if (extensionType == typeof(Services.PrimitiveImpls.MainViewExtension))
        {
            MainViewModel.Execute(new ContextCommandExecution(commandName));
            return;
        }

        // ContextCommand は本来、フォーカスされた InputElement の DataContext から
        // 解決される。パレットでは候補をたどって順に Execute を呼ぶ。
        // 各 handler は default ブランチで Handled=false を返す慣習なので、
        // 該当しないコマンドは安全に no-op になる。
        var execution = new ContextCommandExecution(commandName);
        foreach (IContextCommandHandler handler in EnumerateActiveHandlers(extensionType, commandName))
        {
            handler.Execute(execution);
        }
    }

    private static bool CanInvokeContextCommand(Type extensionType, string commandName)
    {
        if (extensionType == typeof(Services.PrimitiveImpls.MainViewExtension))
            return true;

        return EnumerateActiveHandlers(extensionType, commandName).Any();
    }

    // ElementViewModel.Execute が処理する element-level コマンド。
    // TimelineTabViewModel.Execute は default ブランチで no-op になる。
    private static readonly HashSet<string> s_timelineElementLevelCommands =
        new(StringComparer.Ordinal) { "Rename", "Split" };

    private static IEnumerable<IContextCommandHandler> EnumerateActiveHandlers(
        Type extensionType, string commandName)
    {
        // 1. 選択中の Editor タブの Context (例: EditViewModel)
        IEditorContext? tabCtx = EditorService.Current.SelectedTabItem.Value?.Context.Value;
        if (tabCtx is IContextCommandHandler tabHandler)
            yield return tabHandler;

        if (tabCtx is not EditViewModel editVm)
            yield break;

        bool isTimelineElementLevel =
            extensionType == typeof(Services.PrimitiveImpls.TimelineTabExtension)
            && s_timelineElementLevelCommands.Contains(commandName);

        // 2. extensionType と一致する ToolTab の Context
        // element-level コマンド (Rename / Split) は ElementViewModel が処理するので、
        // TimelineTabViewModel は候補から除外する (default ブランチで no-op になる重複呼び出しを避ける)。
        if (!isTimelineElementLevel)
        {
            foreach (BeutlToolDockable tool in editVm.DockHost.Factory.EnumerateTools())
            {
                if (tool.ToolContext is IContextCommandHandler toolHandler
                    && tool.ToolContext.Extension.GetType() == extensionType)
                {
                    yield return toolHandler;
                }
            }

            yield break;
        }

        // 3. TimelineTab の選択中 Element (Rename / Split など element-level コマンド向け)
        // ElementViewModel.OnSplit などは GetGroupOrSelectedElements() で全選択要素を処理するため、
        // 全要素ぶん handler を呼ぶと多重実行になる。先頭 1 件のみを候補にする。
        TimelineTabViewModel? timeline = editVm.FindToolTab<TimelineTabViewModel>();
        ElementViewModel? first = timeline?.SelectedElements.FirstOrDefault();
        if (first != null)
            yield return first;
    }

    private static OSPlatform GetCurrentPlatform()
    {
        return OperatingSystem.IsWindows() ? OSPlatform.Windows :
            OperatingSystem.IsMacOS() ? OSPlatform.OSX :
            OperatingSystem.IsLinux() ? OSPlatform.Linux :
            OSPlatform.Windows;
    }

    private static string ResolveExtensionDisplayName(Type extensionType)
    {
        if (extensionType == typeof(Services.PrimitiveImpls.MainViewExtension))
            return Strings.CommandPalette_General;

        Extension? ext = ExtensionProvider.Current.AllExtensions
            .FirstOrDefault(e => e.GetType() == extensionType);
        return ext?.DisplayName ?? extensionType.Name;
    }
}

public sealed record CommandPaletteResult(CommandPaletteEntry Entry, int Score)
{
    public string DisplayName => Entry.DisplayName;
    public string Category => Entry.Category;
    public string? KeyGesture => Entry.KeyGesture;
    public bool HasKeyGesture => !string.IsNullOrEmpty(Entry.KeyGesture);
    public bool CanExecuteNow => Entry.CanExecuteNow;
}

internal static class FuzzyMatcher
{
    // 簡易ファジー: 部分文字列 / サブシーケンス / 単語先頭マッチに加点
    public static int Score(string query, string target, string category)
    {
        if (string.IsNullOrEmpty(query)) return 1;
        if (string.IsNullOrEmpty(target)) return 0;

        int best = ScoreCore(query, target, isPrimary: true);
        int catScore = ScoreCore(query, category, isPrimary: false);
        return Math.Max(best, catScore);
    }

    private static int ScoreCore(string query, string text, bool isPrimary)
    {
        int score = 0;

        // 完全一致
        if (string.Equals(query, text, StringComparison.OrdinalIgnoreCase))
            score = Math.Max(score, isPrimary ? 1000 : 500);

        // 接頭辞一致
        if (text.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            score = Math.Max(score, isPrimary ? 800 : 400);

        // 部分一致
        int idx = text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            int s = isPrimary ? 500 : 250;
            // 単語先頭であればボーナス
            if (idx == 0 || !char.IsLetterOrDigit(text[idx - 1]))
                s += isPrimary ? 100 : 50;
            score = Math.Max(score, s);
        }

        // サブシーケンス一致
        int subseq = SubsequenceScore(query, text);
        if (subseq > 0)
        {
            int s = isPrimary ? 100 + subseq : 50 + subseq / 2;
            score = Math.Max(score, s);
        }

        return score;
    }

    private static int SubsequenceScore(string query, string text)
    {
        int qi = 0;
        int score = 0;
        bool prevMatched = false;
        bool prevIsBoundary = true;

        for (int i = 0; i < text.Length && qi < query.Length; i++)
        {
            char tc = char.ToLowerInvariant(text[i]);
            char qc = char.ToLowerInvariant(query[qi]);
            if (tc == qc)
            {
                score += 1;
                if (prevMatched) score += 2; // 連続マッチ
                if (prevIsBoundary) score += 3; // 単語先頭
                qi++;
                prevMatched = true;
            }
            else
            {
                prevMatched = false;
            }
            prevIsBoundary = !char.IsLetterOrDigit(text[i]);
        }

        return qi == query.Length ? score : 0;
    }
}
