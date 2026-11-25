using System.Reactive;
using System.Text.Json.Nodes;
using Beutl.Controls;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Services;
using Beutl.Logging;
using Beutl.ProjectSystem;
using Beutl.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.UndisplayableDataTab.ViewModels;

public sealed class UndisplayableDataRow
{
    internal UndisplayableDataRow(UndisplayableDataEntry entry)
    {
        Entry = entry;
    }

    internal UndisplayableDataEntry Entry { get; }
    public string Breadcrumb => Entry.Breadcrumb;
    public string Label => UndisplayableDataEntry.GetKindLabel(Entry.Kind, Entry.IsUnreadableElement, Entry.IsLegacyNodeTree);
    public StatusLabelKind Status => Entry.Kind == UndisplayableDataKind.DeserializationFailed
        ? StatusLabelKind.Critical : StatusLabelKind.Caution;
    public bool CanLocate => Entry.Scene is not null;
    public bool IsRecoveryProtected => Entry.IsRecoveryProtected;
    public string? TypeName => Entry.TypeName;
    public string UnknownKeys => string.Join(", ", Entry.UnknownKeys);
    public string? ErrorMessage => Entry.ErrorMessage;
    public string? SourceFilePath => Entry.SourceFilePath;
    public bool HasPreview => Entry.HasPreview;
    public string PreviewLabel => Entry.IsUnreadableElement
        ? UndisplayableDataStrings.OriginalFileContents : UndisplayableDataStrings.JsonPreview;
    public ReactivePropertySlim<bool> IsExpanded { get; } = new();
    public ReactivePropertySlim<string?> Preview { get; } = new();

    internal string LoadPreview() => Preview.Value ??= Entry.GetPreview();

    internal void RefreshLoadedPreview()
    {
        if (Preview.Value is not null && Entry.Json is not null) Preview.Value = Entry.GetPreview();
    }

    internal bool Matches(UndisplayableDataEntry entry)
        => (Entry with { UnknownKeys = entry.UnknownKeys }) == entry
           && Entry.UnknownKeys.SequenceEqual(entry.UnknownKeys);
}

public sealed class UndisplayableDataViewModel : IToolContext
{
    private static readonly ILogger s_logger = Log.CreateLogger<UndisplayableDataViewModel>();
    private readonly IUndisplayableDataNavigator _navigator;
    private readonly Scene _scene;
    private readonly CompositeDisposable _disposables = [];
    private bool _disposed;

    internal UndisplayableDataViewModel(IEditorContext editor, Scene scene, IUndisplayableDataNavigator navigator)
    {
        _scene = scene;
        _navigator = navigator;

        var projects = Observable.Merge(
                Observable.FromEventPattern<HierarchyAttachmentEventArgs>(
                    handler => scene.AttachedToHierarchy += handler, handler => scene.AttachedToHierarchy -= handler),
                Observable.FromEventPattern<HierarchyAttachmentEventArgs>(
                    handler => scene.DetachedFromHierarchy += handler, handler => scene.DetachedFromHierarchy -= handler))
            .ObserveOnUIDispatcher()
            .Select(_ => scene.FindHierarchicalParent<Project>())
            .StartWith(scene.FindHierarchicalParent<Project>())
            .DistinctUntilChanged();

        var edits = projects.CombineLatest(CurrentSceneOnly, (project, currentOnly) => currentOnly ? null : project)
            .Select(WatchScope)
            .Switch();
        var history = editor.GetService<HistoryManager>()?.StateChanged.Select(_ => Unit.Default) ?? Observable.Empty<Unit>();
        edits.Merge(history)
            .Throttle(TimeSpan.FromMilliseconds(250))
            .ObserveOnUIDispatcher()
            .Subscribe(_ => Refresh())
            .DisposeWith(_disposables);
        CurrentSceneOnly.Skip(1).Subscribe(_ => Refresh()).DisposeWith(_disposables);
        Refresh();
    }

    public ToolTabExtension Extension => UndisplayableDataTabExtension.Instance;
    public IReactiveProperty<bool> IsSelected { get; } = new ReactivePropertySlim<bool>();
    public IReadOnlyReactiveProperty<string> Header { get; } = new ReactivePropertySlim<string>(UndisplayableDataStrings.Title);
    public ReactivePropertySlim<IReadOnlyList<UndisplayableDataRow>> Rows { get; } = new([]);
    public ReactivePropertySlim<bool> CurrentSceneOnly { get; } = new();
    public ReactivePropertySlim<bool> HasProject { get; } = new();
    public ReactivePropertySlim<bool> IsEmpty { get; } = new();
    public ReactivePropertySlim<bool> ScanFailed { get; } = new();

    public void Refresh()
    {
        if (_disposed) return;
        Project? project = _scene.FindHierarchicalParent<Project>();
        HasProject.Value = project is not null;
        IReadOnlyList<UndisplayableDataEntry> entries;
        try
        {
            entries = project is not null && !CurrentSceneOnly.Value
                ? UndisplayableDataScanner.Scan(project, _scene)
                : UndisplayableDataScanner.Scan(_scene);
            ScanFailed.Value = false;
        }
        catch (Exception ex) when (!ExceptionHelpers.ContainsFatalFailure(ex))
        {
            s_logger.LogWarning(ex, "Failed to inspect undisplayable data in scene {SceneId}", _scene.Id.Value);
            ScanFailed.Value = true;
            entries = [];
        }

        // Keep the displayed rows when an unrelated edit leaves the diagnostics unchanged or shifts indices.
        var oldRows = Rows.Value;
        var reused = new bool[oldRows.Count];
        var rows = new UndisplayableDataRow[entries.Count];
        for (int i = 0; i < rows.Length; i++)
        {
            var entry = entries[i];
            int match = -1;
            if (i < oldRows.Count && !reused[i] && oldRows[i].Matches(entry))
            {
                match = i;
            }
            else
            {
                for (int j = 0; j < oldRows.Count; j++)
                {
                    if (!reused[j] && oldRows[j].Matches(entry))
                    {
                        match = j;
                        break;
                    }
                }
            }

            if (match >= 0)
            {
                reused[match] = true;
                rows[i] = oldRows[match];
                rows[i].RefreshLoadedPreview();
            }
            else
            {
                rows[i] = new(entry);
            }
        }
        if (!oldRows.SequenceEqual(rows)) Rows.Value = rows;
        IsEmpty.Value = entries.Count == 0 && !ScanFailed.Value;
    }

    public void Locate(UndisplayableDataRow row)
    {
        if (!_disposed && row.CanLocate) _navigator.Locate(row.Entry);
    }

    private IObservable<Unit> WatchScope(Project? project)
    {
        if (project is null) return WatchScene(_scene).StartWith(Unit.Default);
        return project.Items.CollectionChangedAsObservable()
            .Select(_ => Unit.Default)
            .StartWith(Unit.Default)
            .Select(_ => project.Items.OfType<Scene>().Distinct().Select(WatchScene).Merge().StartWith(Unit.Default))
            .Switch();
    }

    private static IObservable<Unit> WatchScene(Scene scene)
        => Observable.Merge(
            Observable.FromEventPattern(
                handler => scene.Edited += handler, handler => scene.Edited -= handler).Select(_ => Unit.Default),
            scene.Layers.CollectionChangedAsObservable().Select(_ => Unit.Default),
            scene.Markers.CollectionChangedAsObservable().Select(_ => Unit.Default));

    public void Dispose()
    {
        _disposed = true;
        _disposables.Dispose();
        Rows.Dispose();
        CurrentSceneOnly.Dispose();
        HasProject.Dispose();
        IsEmpty.Dispose();
        ScanFailed.Dispose();
        IsSelected.Dispose();
        Header.Dispose();
    }

    public object? GetService(Type serviceType) => null;
    public void WriteToJson(JsonObject json) { }
    public void ReadFromJson(JsonObject json) { }
}
