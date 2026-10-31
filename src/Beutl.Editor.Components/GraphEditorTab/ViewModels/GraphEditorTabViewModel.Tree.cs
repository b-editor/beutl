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
            var seenAnimations = new HashSet<IKeyFrameAnimation>();
            foreach (string key in used)
            {
                var item = _treeCache[key];
                if (item.PropertyItem != null || item.Animation is not IKeyFrameAnimation animation || !seenAnimations.Add(animation)) continue;
                animations.Add(Items.FirstOrDefault(x => x.Object == animation && x.Name == item.Name.Value
                    && x.AnimationOwner == item.AnimationOwner)
                    ?? new GraphEditorItemViewModel(item.Name.Value, animation) { AnimationOwner = item.AnimationOwner });
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
                    AnimationOperations.ObserveTimeOrigin(item.Object, item.AnimationOwner).Skip(1)
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
        if (value is null or string or IKeyFrameAnimation || !ancestors.Add(value)) return;
        try
        {
            if (value is INodeMember { Property: { } adapter } member)
            {
                var item = GetTreeItem($"{path}/port:{member.Id.Value}", adapter.DisplayName, parent, used);
                item.Adapter = adapter;
                item.AnimationOwner = member.FindHierarchicalParent<GraphNode>();
                UpdatePropertyItem(item, adapter is IAnimatablePropertyAdapter && !adapter.IsReadOnly, used, ancestors);
                result.Add(item);
            }
            else if (value is EngineObject obj)
            {
                var item = GetTreeItem($"{path}/object:{obj.Id.Value}", string.IsNullOrWhiteSpace(obj.Name.Value)
                    ? TypeDisplayHelpers.GetLocalizedName(obj.GetType()) : obj.Name.Value, parent, used);
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
                AppendValue(list.Value, parent, path, children, used, ancestors);
                continue;
            }
            var item = GetTreeItem(path, Property.GetLocalizedName(property), parent, used);
            item.Property = property;
            UpdatePropertyItem(item, property is IExpressionProperty { IsAnimatable: true }, used, ancestors);
            children.Add(item);
        }
        AppendCoreChildren(obj, parent, parent.Key, children, used, ancestors);
        Synchronize(parent.Children, children);
    }

    private void AppendCoreChildren(CoreObject obj, GraphEditorTreeItemViewModel? parent, string path,
        List<GraphEditorTreeItemViewModel> result, HashSet<string> used, HashSet<object> ancestors)
    {
        foreach (IProperty field in obj.Properties.Where(obj.IsBaseField))
        {
            if (field.ValueType.IsValueType || field.ValueType == typeof(string)) continue;
            AppendValue(field.Value, parent, $"{path}/core:{field.Name}", result, used, ancestors);
        }
    }

    private void UpdatePropertyItem(GraphEditorTreeItemViewModel item, bool canAnimate, HashSet<string> used, HashSet<object> ancestors)
    {
        var engineProperty = item.Property ?? item.Adapter?.GetEngineProperty();
        var componentAnimation = engineProperty?.GetPropertyAnimation() as IComponentAnimation;
        item.CanSeparateComponents.Value = canAnimate && ComponentSeparation.CanSeparate(engineProperty);
        item.HasSeparatedComponents.Value = componentAnimation != null;
        if (componentAnimation != null)
        {
            item.CanAnimate.Value = false;
            item.CanRemoveAnimation.Value = false;
            item.HasAnimation.Value = true;
            var components = new List<GraphEditorTreeItemViewModel>();
            foreach (var property in componentAnimation.Components)
            {
                var child = GetTreeItem($"{item.Key}/component:{property.Name}", Property.GetLocalizedName(property), item, used);
                child.Property = property;
                child.AnimationOwner = item.AnimationOwner;
                UpdatePropertyItem(child, canAnimate, used, ancestors);
                components.Add(child);
            }
            Synchronize(item.Children, components);
            return;
        }
        var factory = item.ValueType is { } valueType ? GraphEditorViewViewModelFactory.GetFactory(valueType) : null;
        item.CanAnimate.Value = canAnimate && factory != null;
        // Removing an animation that holds what could not be loaded repairs a recovery-protected clip.
        item.CanRemoveAnimation.Value = item.Animation != null && ElementEditability.CanRepair(Element.Value, item.Animation);
        item.HasAnimation.Value = item.Animation != null;
        var children = new List<GraphEditorTreeItemViewModel>();
        if (item.ValueType is { } type)
        {
            var channels = factory?.ChannelNames;
            if (channels is { Count: > 1 })
                foreach (string name in channels)
                {
                    var channel = GetTreeItem($"{item.Key}/channel:{name}", GraphEditorViewViewModel.GetDisplayName(name), item, used);
                    channel.PropertyItem = item;
                    if (channel.ChannelName != name)
                    {
                        channel.ChannelName = name;
                        channel.ChannelBrush.Value = new SolidColorBrush(GraphEditorViewViewModel.GetChannelColor(name));
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
        SelectedItem.Value = property?.Animation is IKeyFrameAnimation animation ? Items.FirstOrDefault(x => x.Object == animation) : null;
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
        Rational rate = (element?.FindHierarchicalParent<Project>()).GetFrameRate();
        MediaTime globalTime = _clock.CurrentTime.Value;
        foreach (var item in _treeCache.Values)
        {
            var property = item.PropertyItem ?? item;
            if (property.Animation is not IKeyFrameAnimation animation)
            {
                item.HasKeyFrame.Value = false;
                continue;
            }
            MediaTime keyTime = (globalTime - AnimationOperations.GetTimeOrigin(animation, property.AnimationOwner))
                .RoundToFrame(rate, MidpointRounding.AwayFromZero);
            item.HasKeyFrame.Value = AnimationOperations.FindKeyFrameOnFrame(animation.KeyFrames, keyTime, rate) != null;
        }
    }

    public void ToggleComponentSeparation(GraphEditorTreeItemViewModel item)
    {
        if (!item.CanSeparateComponents.Value || Element.Value == null || !ElementEditability.IsEditable(Element.Value)
            || !_treeCache.TryGetValue(item.Key, out var current) || current != item
            || (item.Property ?? item.Adapter?.GetEngineProperty()) is not { } property) return;

        bool separated = property.GetPropertyAnimation() is IComponentAnimation;
        _editorContext.GetRequiredService<HistoryManager>().ExecuteInTransaction(() =>
        {
            if (separated) ComponentAnimations.Unseparate(property, _clock.CurrentTime.Value);
            else ComponentAnimations.Separate(property);
        }, separated ? Strings.UnseparateComponents : Strings.SeparateComponents);
        Refresh();
        ExpandAncestors(item);
        if (!separated) item.IsExpanded.Value = true;
        SelectedTreeItem.Value = separated ? item
            : item.Children.FirstOrDefault(child => child.Animation != null) ?? item.Children.FirstOrDefault() ?? item;
    }

    public void ToggleKeyFrame(GraphEditorTreeItemViewModel item)
    {
        var property = item.PropertyItem ?? item;
        if (!property.CanAnimate.Value || !_treeCache.TryGetValue(property.Key, out var current) || current != property) return;
        if (property.Animation == null)
        {
            EnableAnimation(item);
            return;
        }
        if (property.Animation is not IKeyFrameAnimation animation) return;
        SelectedTreeItem.Value = item;
        SelectTreeItem(item);
        if (SelectedAnimation.Value is not { } graph) return;
        MediaTime globalTime = _clock.CurrentTime.Value;
        MediaTime keyTime = graph.ConvertKeyTime(globalTime);
        IKeyFrame? keyFrame = AnimationOperations.FindKeyFrameOnFrame(animation.KeyFrames, keyTime, AnimationOperations.GetFrameRate(graph.Scene));
        if (keyFrame != null) graph.DeleteKeyFrames([keyFrame]);
        else graph.InsertKeyFrame(new SplineEasing(), globalTime);
        UpdateKeyFrameStates();
    }

    public void RemoveAnimation(GraphEditorTreeItemViewModel item)
    {
        var property = item.PropertyItem ?? item;
        if (!property.CanRemoveAnimation.Value || property.Animation == null
            || !ElementEditability.CanRepair(Element.Value, property.Animation)
            || !_treeCache.TryGetValue(property.Key, out var current) || current != property) return;
        HistoryManager history = _editorContext.GetRequiredService<HistoryManager>();
        history.ExecuteInTransaction(() =>
        {
            property.Animation = null;
            if (Element.Value is { } element) ElementRecoveryService.TryCompleteRepair(element, history);
        }, Strings.RemoveAnimation);
        Refresh();
    }

    public void EnableAnimation(GraphEditorTreeItemViewModel item)
    {
        var property = item.PropertyItem ?? item;
        if (!property.CanAnimate.Value || Element.Value == null || property.ValueType is not { } type
            || !ElementEditability.IsEditable(Element.Value)
            || !_treeCache.TryGetValue(property.Key, out var current) || current != property) return;
        if (property.Animation == null)
        {
            var animation = (IKeyFrameAnimation)Activator.CreateInstance(typeof(KeyFrameAnimation<>).MakeGenericType(type))!;
            var key = (IKeyFrame)Activator.CreateInstance(typeof(KeyFrame<>).MakeGenericType(type))!;
            key.Value = property.Value;
            key.Easing.Value = new SplineEasing();
            key.KeyTime.Value = MediaTime.Zero;
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
