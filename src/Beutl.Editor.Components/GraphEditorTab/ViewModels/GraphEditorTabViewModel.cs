using System.Text.Json.Nodes;
using Avalonia.Threading;
using Beutl.Animation;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Services;
using Beutl.ProjectSystem;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;

namespace Beutl.Editor.Components.GraphEditorTab.ViewModels;

public sealed record GraphEditorItemViewModel(string Name, KeyFrameAnimation Object);

public sealed partial class GraphEditorTabViewModel : IToolContext
{
    private readonly IEditorContext _editorContext;
    private readonly IEditorClock _clock;
    private readonly CompositeDisposable _disposables = [];
    private readonly CompositeDisposable _animationDisposables = [];
    private GraphEditorViewModel? _activeGraph;
    private bool _disposed;

    public GraphEditorTabViewModel(IEditorContext editorContext)
    {
        _editorContext = editorContext;
        _clock = editorContext.GetRequiredService<IEditorClock>();
        // Element の DetachedFromHierarchy を購読
        Element.CombineWithPrevious()
            .Subscribe(v =>
            {
                if (v.OldValue is IHierarchical old)
                    old.DetachedFromHierarchy -= OnElementDetached;
                if (v.NewValue is IHierarchical @new)
                    @new.DetachedFromHierarchy += OnElementDetached;
                if (v.OldValue != null) v.OldValue.Edited -= OnElementEdited;
                if (v.NewValue != null) v.NewValue.Edited += OnElementEdited;
            })
            .DisposeWith(_disposables);

        SelectedAnimation = SelectedItem.CombineLatest(Element)
            .Select(t =>
            {
                if (t.First == null || t.Second == null) return null;

                Type type = t.First.Object.ValueType;
                Type viewModelType = typeof(GraphEditorViewModel<>).MakeGenericType(type);
                return (GraphEditorViewModel)Activator.CreateInstance(viewModelType, _editorContext, t.First.Object, t.Second)!;
            })
            .Do(graph =>
            {
                var previous = _activeGraph;
                _activeGraph = graph;
                if (previous != null) Dispatcher.UIThread.Post(previous.Dispose);
            })
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        Header = Element
            .Select(ToolTabHeaderHelper.ObserveElementLabel)
            .Switch()
            .CombineLatest(SelectedItem, (element, item) => (element, property: item?.Name))
            .Select(t => (t.element, t.property) switch
            {
                ({ Length: > 0 } e, { Length: > 0 } p) => ToolTabHeaderHelper.Compose(e, p),
                ({ Length: > 0 } e, _) => ToolTabHeaderHelper.Compose(Strings.GraphEditor, e),
                (_, { Length: > 0 } p) => ToolTabHeaderHelper.Compose(Strings.GraphEditor, p),
                _ => Strings.GraphEditor,
            })
            .ToReadOnlyReactivePropertySlim(Strings.GraphEditor)
            .DisposeWith(_disposables)!;

        SelectedTreeItem.Subscribe(item =>
        {
            if (!_synchronizingTree) SelectTreeItem(item);
        }).DisposeWith(_disposables);
        Element.Subscribe(_ => Refresh()).DisposeWith(_disposables);
        _clock.CurrentTime.Subscribe(_ => UpdateKeyFrameStates()).DisposeWith(_disposables);
    }

    public IReadOnlyReactiveProperty<string> Header { get; }

    public ToolTabExtension Extension => GraphEditorTabExtension.Instance;

    public ReadOnlyReactivePropertySlim<GraphEditorViewModel?> SelectedAnimation { get; }

    public ReactivePropertySlim<GraphEditorItemViewModel?> SelectedItem { get; } = new();

    public ReactiveProperty<Element?> Element { get; } = new();

    public CoreList<GraphEditorItemViewModel> Items { get; } = [];

    public IReactiveProperty<bool> IsSelected { get; } = new ReactiveProperty<bool>();

    public void Dispose()
    {
        _disposed = true;
        if (Element.Value is IHierarchical h)
            h.DetachedFromHierarchy -= OnElementDetached;
        if (Element.Value is { } element) element.Edited -= OnElementEdited;
        _animationDisposables.Dispose();
        _disposables.Dispose();
        _activeGraph?.Dispose();
        foreach (var item in _treeCache.Values) item.Dispose();
        _treeCache.Clear();
        SelectedTreeItem.Dispose();
    }

    public object? GetService(Type serviceType)
    {
        return null;
    }

    public void Refresh()
    {
        if (!_disposed) RefreshTree();
    }

    private void OnElementDetached(object? sender, HierarchyAttachmentEventArgs e)
    {
        if (_disposed) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (!_disposed)
                _editorContext.CloseToolTab(this);
        });
    }

    private void OnAnimationDetached(object? sender, HierarchyAttachmentEventArgs e)
    {
        ScheduleRefresh();
    }

    /// <summary>
    /// Finds a matching or idle graph-editor tab for <paramref name="animation"/>.
    /// </summary>
    /// <remarks>
    /// Occupied tabs are not retargeted; callers create a new tab when needed.
    /// </remarks>
    public static GraphEditorTabViewModel? FindReusable(
        IEditorContext editorContext, Element? element, KeyFrameAnimation? animation)
    {
        return ToolTabReuse.Find<GraphEditorTabViewModel>(
            editorContext,
            t => t.Element.Value == element && t.SelectedItem.Value?.Object == animation,
            t => t.Element.Value is null,
            retargetAnyOpen: false);
    }

    public void Select(KeyFrameAnimation? animation)
    {
        if (animation == null)
        {
            SelectedTreeItem.Value = null;
            SelectedItem.Value = null;
        }
        else
        {
            Refresh();
            var item = _treeCache.Values.FirstOrDefault(i => i.PropertyItem == null && i.Animation == animation);
            if (item != null)
            {
                ExpandAncestors(item);
                SelectedTreeItem.Value = item;
                SelectTreeItem(item);
            }
        }
    }

    public void ReadFromJson(JsonObject json)
    {
        try
        {
            var scene = _editorContext.GetRequiredService<Scene>();
            if (json.TryGetPropertyValueAsJsonValue("elementId", out Guid elmId)
                && scene.FindById(elmId) is Element elm)
            {
                Element.Value = elm;
                if (json.TryGetPropertyValueAsJsonValue("propertyPath", out string? path)
                    && path != null && _treeCache.TryGetValue(path, out var item))
                {
                    ExpandAncestors(item);
                    SelectedTreeItem.Value = item;
                    SelectTreeItem(item);
                }
                else if (json.TryGetPropertyValueAsJsonValue("animationId", out Guid anmId))
                {
                    Select(Items.FirstOrDefault(i => i.Object.Id == anmId)?.Object);
                }
            }
        }
        catch
        {
        }
    }

    public void WriteToJson(JsonObject json)
    {
        if (Element.Value is { } element) json["elementId"] = element.Id;
        else json.Remove("elementId");
        if (SelectedAnimation.Value is { Animation: ICoreObject { Id: var anmId } }) json["animationId"] = anmId;
        else json.Remove("animationId");
        if (SelectedTreeItem.Value is { } item) json["propertyPath"] = item.Key;
        else json.Remove("propertyPath");
    }
}
