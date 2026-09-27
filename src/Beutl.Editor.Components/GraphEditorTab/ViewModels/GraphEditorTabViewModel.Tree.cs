using System.Collections;
using Avalonia.Media;
using Avalonia.Threading;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Extensibility;
using Beutl.NodeGraph;
using Beutl.ProjectSystem;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.GraphEditorTab.ViewModels;

public sealed partial class GraphEditorTabViewModel
{
    private readonly Dictionary<string, GraphEditorTreeItemViewModel> _treeCache = [];
    private bool _synchronizingTree;
    private bool _refreshPending;

    public CoreList<GraphEditorTreeItemViewModel> TreeItems { get; } = [];
    public ReactivePropertySlim<GraphEditorTreeItemViewModel?> SelectedTreeItem { get; } = new();

    private void OnElementEdited(object? sender, EventArgs e) => ScheduleRefresh();

    private void ScheduleRefresh()
    {
        if (_disposed || _refreshPending) return;
        _refreshPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _refreshPending = false;
            Refresh();
        });
    }

    private void RefreshTree()
    {
        if (_synchronizingTree) return;
        _synchronizingTree = true;
        string? selectedKey = SelectedTreeItem.Value?.Key;
        var used = new HashSet<string>();
        var ancestors = new HashSet<object>(ReferenceEqualityComparer.Instance);
        try
        {
            var roots = new List<GraphEditorTreeItemViewModel>();
            if (Element.Value is { } element)
                foreach (var obj in element.Objects)
                    AppendValue(obj, null, "", roots, used, ancestors);
            Synchronize(TreeItems, roots);

            var animations = new List<GraphEditorItemViewModel>();
            var seenAnimations = new HashSet<KeyFrameAnimation>();
            foreach (string key in used)
            {
                var item = _treeCache[key];
                if (item.PropertyItem != null || item.Animation is not KeyFrameAnimation animation || !seenAnimations.Add(animation)) continue;
                animations.Add(Items.FirstOrDefault(x => x.Object == animation && x.Name == item.Name.Value)
                    ?? new GraphEditorItemViewModel(item.Name.Value, animation));
            }
            if (!Items.SequenceEqual(animations))
            {
                _animationDisposables.Clear();
                Items.Clear();
                Items.AddRange(animations);
                foreach (var item in Items)
                {
                    item.Object.DetachedFromHierarchy += OnAnimationDetached;
                    _animationDisposables.Add(Disposable.Create(item.Object, obj => obj.DetachedFromHierarchy -= OnAnimationDetached));
                    item.Object.GetObservable(KeyFrameAnimation.UseGlobalClockProperty).Skip(1)
                        .Subscribe(_ => UpdateKeyFrameStates()).DisposeWith(_animationDisposables);
                    item.Object.KeyFrames.CollectionChangedAsObservable()
                        .Subscribe(_ => UpdateKeyFrameStates()).DisposeWith(_animationDisposables);
                }
            }

            SelectedTreeItem.Value = selectedKey != null && used.Contains(selectedKey) ? _treeCache[selectedKey] : null;
            foreach (string key in _treeCache.Keys.Where(x => !used.Contains(x)).ToArray())
            {
                _treeCache[key].Dispose();
                _treeCache.Remove(key);
            }
        }
        finally
        {
            _synchronizingTree = false;
        }
        SelectTreeItem(SelectedTreeItem.Value);
        UpdateKeyFrameStates();
    }

    private GraphEditorTreeItemViewModel GetTreeItem(string key, string name, GraphEditorTreeItemViewModel? parent, HashSet<string> used)
    {
        used.Add(key);
        if (!_treeCache.TryGetValue(key, out var item))
            _treeCache[key] = item = new GraphEditorTreeItemViewModel(key, parent);
        item.Name.Value = name;
        return item;
    }

    private void AppendValue(object? value, GraphEditorTreeItemViewModel? parent, string path,
        List<GraphEditorTreeItemViewModel> result, HashSet<string> used, HashSet<object> ancestors)
    {
        if (value is IOptional optional) value = optional.ToObject().GetValueOrDefault();
        if (value is null or string or IAnimation || !ancestors.Add(value)) return;
        try
        {
            if (value is INodeMember { Property: { } adapter } member)
            {
                var item = GetTreeItem($"{path}/port:{member.Id}", adapter.DisplayName, parent, used);
                item.Adapter = adapter;
                UpdatePropertyItem(item, adapter is IAnimatablePropertyAdapter && !adapter.IsReadOnly, used, ancestors);
                result.Add(item);
            }
            else if (value is EngineObject obj)
            {
                var item = GetTreeItem($"{path}/object:{obj.Id}", string.IsNullOrWhiteSpace(obj.Name)
                    ? TypeDisplayHelpers.GetLocalizedName(obj.GetType()) : obj.Name, parent, used);
                UpdateObjectChildren(obj, item, used, ancestors);
                result.Add(item);
            }
            else if (value is CoreObject core)
            {
                AppendCoreChildren(core, parent, path, result, used, ancestors);
            }
            else if (value is IEnumerable list)
            {
                foreach (object? child in list) AppendValue(child, parent, path, result, used, ancestors);
            }
        }
        finally
        {
            ancestors.Remove(value);
        }
    }

    private void UpdateObjectChildren(EngineObject obj, GraphEditorTreeItemViewModel parent, HashSet<string> used, HashSet<object> ancestors)
    {
        var children = new List<GraphEditorTreeItemViewModel>();
        foreach (IProperty property in obj.GetDisplayProperties())
        {
            string path = $"{parent.Key}/property:{property.Name}";
            if (property is IListProperty { Name: "Children" } list
                && typeof(EngineObject).IsAssignableFrom(list.ElementType))
            {
                // Flatten the collection row while retaining property paths used by saved selections.
                AppendValue(list.CurrentValue, parent, path, children, used, ancestors);
                continue;
            }
            var item = GetTreeItem(path, Property.GetLocalizedName(property), parent, used);
            item.Property = property;
            UpdatePropertyItem(item, property.IsAnimatable, used, ancestors);
            children.Add(item);
        }
        AppendCoreChildren(obj, parent, parent.Key, children, used, ancestors);
        Synchronize(parent.Children, children);
    }

    private void AppendCoreChildren(CoreObject obj, GraphEditorTreeItemViewModel? parent, string path,
        List<GraphEditorTreeItemViewModel> result, HashSet<string> used, HashSet<object> ancestors)
    {
        foreach (var property in PropertyRegistry.GetRegistered(obj.GetType()))
        {
            if (property == Hierarchical.HierarchicalParentProperty || property.PropertyType.IsValueType
                || property.PropertyType == typeof(string)) continue;
            AppendValue(obj.GetValue(property), parent, $"{path}/core:{property.Name}", result, used, ancestors);
        }
    }

    private void UpdatePropertyItem(GraphEditorTreeItemViewModel item, bool canAnimate, HashSet<string> used, HashSet<object> ancestors)
    {
        item.CanAnimate.Value = canAnimate;
        item.HasAnimation.Value = item.Animation is KeyFrameAnimation;
        var children = new List<GraphEditorTreeItemViewModel>();
        if (item.ValueType is { } type)
        {
            var channels = GraphEditorViewViewModelFactory.GetFactory(type).FirstOrDefault()?.ChannelNames;
            if (channels is { Count: > 1 })
                foreach (string name in channels)
                {
                    var channel = GetTreeItem($"{item.Key}/channel:{name}", GraphEditorViewViewModel.GetDisplayName(name), item, used);
                    channel.PropertyItem = item;
                    if (channel.ChannelName != name)
                    {
                        channel.ChannelName = name;
                        channel.ChannelBrush.Value = new SolidColorBrush(name switch
                        {
                            "Red" => Colors.Red,
                            "Green" => Colors.Green,
                            "Blue" => Colors.Blue,
                            "Alpha" => Colors.White,
                            "Y" or "Height" => Color.Parse("#56B88B"),
                            "Z" => Color.Parse("#619FEF"),
                            "W" => Color.Parse("#BE83E8"),
                            _ => Color.Parse("#E87070")
                        });
                    }
                    channel.HasAnimation.Value = item.HasAnimation.Value;
                    children.Add(channel);
                }
        }

        // A property holding a single object (Fill, Transform, ...) is already its label.
        // Put that object's properties directly under it instead of adding an extra type row.
        object? value = item.Value;
        if (value is EngineObject nested && ancestors.Add(nested))
        {
            UpdateObjectChildren(nested, item, used, ancestors);
            children.AddRange(item.Children);
            ancestors.Remove(nested);
        }
        else AppendValue(value, item, item.Key, children, used, ancestors);
        Synchronize(item.Children, children);
    }

    private static void Synchronize(CoreList<GraphEditorTreeItemViewModel> collection, List<GraphEditorTreeItemViewModel> items)
    {
        if (collection.SequenceEqual(items)) return;
        collection.Clear();
        collection.AddRange(items);
    }

    private void SelectTreeItem(GraphEditorTreeItemViewModel? item)
    {
        var property = item?.PropertyItem ?? item;
        SelectedItem.Value = property?.Animation is KeyFrameAnimation animation ? Items.FirstOrDefault(x => x.Object == animation) : null;
        if (SelectedAnimation.Value is not { } graph) return;
        string? channelName = item?.ChannelName;
        if (channelName != null && graph.Views.FirstOrDefault(x => x.Name == channelName) is { } channel)
            graph.SelectedView.Value = channel;
    }

    private static void ExpandAncestors(GraphEditorTreeItemViewModel item)
    {
        for (var parent = item.Parent; parent != null; parent = parent.Parent)
            parent.IsExpanded.Value = true;
    }

    private void UpdateKeyFrameStates()
    {
        if (_disposed) return;
        var element = Element.Value;
        int rate = element?.FindHierarchicalParent<Project>()?.GetFrameRate() ?? 30;
        TimeSpan globalTime = _clock.CurrentTime.Value;
        foreach (var item in _treeCache.Values)
        {
            var property = item.PropertyItem ?? item;
            if (property.Animation is not KeyFrameAnimation animation)
            {
                item.HasKeyFrame.Value = false;
                continue;
            }
            TimeSpan keyTime = (animation.UseGlobalClock ? globalTime : globalTime - (element?.Start ?? TimeSpan.Zero)).RoundToRate(rate);
            item.HasKeyFrame.Value = animation.KeyFrames.Any(key => key.KeyTime == keyTime);
        }
    }

    public void ToggleKeyFrame(GraphEditorTreeItemViewModel item)
    {
        var property = item.PropertyItem ?? item;
        if (!property.CanAnimate.Value || !_treeCache.TryGetValue(property.Key, out var current) || current != property) return;
        if (property.Animation is not KeyFrameAnimation animation)
        {
            EnableAnimation(item);
            return;
        }
        SelectedTreeItem.Value = item;
        SelectTreeItem(item);
        if (SelectedAnimation.Value is not { } graph) return;
        TimeSpan globalTime = _clock.CurrentTime.Value;
        TimeSpan keyTime = graph.ConvertKeyTime(globalTime);
        var keyFrame = animation.KeyFrames.FirstOrDefault(key => key.KeyTime == keyTime);
        if (keyFrame != null) graph.DeleteKeyFrames([keyFrame]);
        else graph.InsertKeyFrame(new SplineEasing(), globalTime);
        UpdateKeyFrameStates();
    }

    public void RemoveAnimation(GraphEditorTreeItemViewModel item)
    {
        var property = item.PropertyItem ?? item;
        if (!property.CanAnimate.Value || property.Animation == null
            || !_treeCache.TryGetValue(property.Key, out var current) || current != property) return;
        _editorContext.GetRequiredService<HistoryManager>().ExecuteInTransaction(() => property.Animation = null, Strings.RemoveAnimation);
        Refresh();
    }

    public void EnableAnimation(GraphEditorTreeItemViewModel item)
    {
        var property = item.PropertyItem ?? item;
        if (!property.CanAnimate.Value || Element.Value == null || property.ValueType is not { } type
            || !_treeCache.TryGetValue(property.Key, out var current) || current != property) return;
        if (property.Animation is not KeyFrameAnimation)
        {
            var animation = (KeyFrameAnimation)Activator.CreateInstance(typeof(KeyFrameAnimation<>).MakeGenericType(type))!;
            var key = (IKeyFrame)Activator.CreateInstance(typeof(KeyFrame<>).MakeGenericType(type))!;
            key.Value = property.Value;
            key.Easing = new SplineEasing();
            key.KeyTime = TimeSpan.Zero;
            animation.KeyFrames.Add(key);
            _editorContext.GetRequiredService<HistoryManager>().ExecuteInTransaction(() =>
            {
                property.Animation = animation;
                property.ClearExpression();
            }, CommandNames.EditKeyFrame);
        }
        Refresh();
        ExpandAncestors(item);
        SelectedTreeItem.Value = item;
        SelectTreeItem(item);
    }
}
