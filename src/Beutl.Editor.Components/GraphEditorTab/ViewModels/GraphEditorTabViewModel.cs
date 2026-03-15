using System.Text.Json.Nodes;
using Avalonia.Threading;
using Beutl.Animation;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.NodeGraph;
using Beutl.ProjectSystem;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;

namespace Beutl.Editor.Components.GraphEditorTab.ViewModels;

public sealed record GraphEditorItemViewModel(string Name, KeyFrameAnimation Object);

public sealed class GraphEditorTabViewModel : IToolContext
{
    private readonly IEditorContext _editorContext;
    private readonly CompositeDisposable _disposables = [];
    private readonly CompositeDisposable _animationDisposables = [];
    private bool _disposed;

    public GraphEditorTabViewModel(IEditorContext editorContext)
    {
        _editorContext = editorContext;
        Element.Subscribe(_ => Refresh()).DisposeWith(_disposables);

        // Element の DetachedFromHierarchy を購読
        Element.CombineWithPrevious()
            .Subscribe(v =>
            {
                if (v.OldValue is IHierarchical old)
                    old.DetachedFromHierarchy -= OnElementDetached;
                if (v.NewValue is IHierarchical @new)
                    @new.DetachedFromHierarchy += OnElementDetached;
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
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);
    }

    public string Header => Strings.GraphEditor;

    public ToolTabExtension Extension => GraphEditorTabExtension.Instance;

    public ReadOnlyReactivePropertySlim<GraphEditorViewModel?> SelectedAnimation { get; }

    public ReactivePropertySlim<GraphEditorItemViewModel?> SelectedItem { get; } = new();

    public ReactiveProperty<Element?> Element { get; } = new();

    public CoreList<GraphEditorItemViewModel> Items { get; } = [];

    public IReactiveProperty<bool> IsSelected { get; } = new ReactiveProperty<bool>();

    public IReactiveProperty<ToolTabExtension.TabPlacement> Placement { get; } =
        new ReactivePropertySlim<ToolTabExtension.TabPlacement>(ToolTabExtension.TabPlacement.LeftLowerBottom);

    public IReactiveProperty<ToolTabExtension.TabDisplayMode> DisplayMode { get; } =
        new ReactivePropertySlim<ToolTabExtension.TabDisplayMode>();

    public void Dispose()
    {
        _disposed = true;
        if (Element.Value is IHierarchical h)
            h.DetachedFromHierarchy -= OnElementDetached;
        _animationDisposables.Dispose();
        _disposables.Dispose();
    }

    public object? GetService(Type serviceType)
    {
        return null;
    }

    public void Refresh()
    {
        var selected = SelectedItem.Value;
        if (Element.Value == null)
        {
            _animationDisposables.Clear();
            Items.Clear();
            return;
        }

        var tmp = new List<GraphEditorItemViewModel>();
        var searcher = new ObjectSearcher(Element.Value, v => v is EngineObject);
        foreach (IProperty prop in searcher.SearchAll().OfType<EngineObject>().SelectMany(o => o.Properties))
        {
            if (prop.Animation is not KeyFrameAnimation anm) continue;

            string name = Property.GetLocalizedName(prop);
            var item = new GraphEditorItemViewModel(
                name,
                anm);
            tmp.Add(item);
        }
        // IAutomaticallyGeneratedPortがついているNodeMemberのプロパティからアニメーションを探す
        searcher = new ObjectSearcher(Element.Value, v => v is IDynamicPort);
        foreach (INodeMember member in searcher.SearchAll().OfType<INodeMember>())
        {
            if (member.Property is not IAnimatablePropertyAdapter { Animation: KeyFrameAnimation anm, DisplayName: { } displayName })
                continue;

            var item = new GraphEditorItemViewModel(displayName, anm);
            tmp.Add(item);
        }


        if (Items.SequenceEqual(tmp)) return;

        _animationDisposables.Clear();
        Items.Clear();
        Items.AddRange(tmp);
        SelectedItem.Value = Items.FirstOrDefault(i => i.Object == selected?.Object);

        // 各アニメーションの DetachedFromHierarchy を購読
        foreach (var item in Items)
        {
            item.Object.DetachedFromHierarchy += OnAnimationDetached;
            _animationDisposables.Add(Disposable.Create(item.Object, obj => obj.DetachedFromHierarchy -= OnAnimationDetached));
        }
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
        if (_disposed) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed) return;
            if (sender is KeyFrameAnimation animation)
            {
                var item = Items.FirstOrDefault(i => i.Object == animation);
                if (item != null)
                {
                    if (SelectedItem.Value?.Object == animation)
                    {
                        SelectedItem.Value = null;
                    }

                    item.Object.DetachedFromHierarchy -= OnAnimationDetached;
                    Items.Remove(item);
                }
            }

            if (Items.Count == 0)
            {
                _editorContext.CloseToolTab(this);
            }
        });
    }

    public void Select(KeyFrameAnimation? animation)
    {
        if (animation == null)
        {
            SelectedItem.Value = null;
        }
        else
        {
            Refresh();
            SelectedItem.Value = Items.FirstOrDefault(i => i.Object == animation);
        }
    }

    public void ReadFromJson(JsonObject json)
    {
        try
        {
            var scene = _editorContext.GetRequiredService<Scene>();
            if (json.TryGetPropertyValueAsJsonValue("elementId", out Guid elmId)
                && json.TryGetPropertyValueAsJsonValue("animationId", out Guid anmId)
                && scene.FindById(elmId) is Element elm)
            {
                Element.Value = elm;
                Select(Items.FirstOrDefault(i => i.Object.Id == anmId)?.Object);
            }
        }
        catch
        {
        }
    }

    public void WriteToJson(JsonObject json)
    {
        if (SelectedAnimation.Value is { Element.Id: { } elmId, Animation: ICoreObject { Id: var anmId } })
        {
            json["elementId"] = elmId;
            json["animationId"] = anmId;
        }
    }
}
