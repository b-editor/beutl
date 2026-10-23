using System.Collections;
using System.Collections.Specialized;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text.Json.Nodes;
using Beutl.Editor;
using Beutl.Engine;
using Beutl.NodeGraph.Composition;
using Beutl.Reactive;
using Beutl.Serialization;

namespace Beutl.NodeGraph.Nodes.Group;

public partial class GroupNode : GraphNode
{
    private readonly CompositeDisposable _disposables = [];
    private readonly Dictionary<INodeMember, IDisposable> _outputPortSubscriptions = [];
    private readonly Dictionary<INodeMember, IDisposable> _inputPortSubscriptions = [];

    public GroupNode()
    {
        ScanProperties<GroupNode>();
        HideProperty(Group);
        Group.Value = new GraphGroup() { Name = { Value = "Group" } };
        Group.Value.Edited += OnGroupEdited;

        Name.GetObservable().Subscribe(v => Group.Value.Name.Value = string.IsNullOrWhiteSpace(v) ? "Group" : v);
        Group.Value.Name.GetObservable().Subscribe(v => Name.Value = v == "Group" ? "" : v);
    }

    private void SynchronizePortSubscriptions(GraphNode? node,
        Dictionary<INodeMember, IDisposable> subscriptions, bool outputSide)
    {
        foreach ((INodeMember source, IDisposable subscription) in subscriptions.ToArray())
        {
            if (node == null || !node.Items.Contains(source))
            {
                subscription.Dispose();
                subscriptions.Remove(source);
            }
        }

        if (node == null) return;
        foreach (INodeMember source in node.Items)
        {
            if (!subscriptions.ContainsKey(source))
                subscriptions.Add(source, MirrorNameAndDisplay(source, outputSide));
        }
    }

    // History restores the inner and outer lists in separate operations. Subscribe to changes only,
    // and find the current mirror when a property changes instead of retaining a removed mirror.
    private CompositeDisposable MirrorNameAndDisplay(INodeMember source, bool outputSide)
    {
        var subscriptions = new CompositeDisposable();
        ((CoreObject)source).Name.GetObservable().Skip(1)
            .Subscribe(_ =>
            {
                if (FindMirror(source, outputSide) is { } mirror) mirror.Name.Value = source.Name.Value;
            }).DisposeWith(subscriptions);
        ((NodeMember)source).Display.GetObservable().Skip(1)
            .Subscribe(display =>
            {
                if (FindMirror(source, outputSide) is NodeMember mirror) mirror.Display.Value = display;
            }).DisposeWith(subscriptions);
        return subscriptions;
    }

    private INodeMember? FindMirror(INodeMember source, bool outputSide)
    {
        int outputCount = Group.Value.Output.Value?.Items.Count ?? 0;
        int inputCount = Group.Value.Input.Value?.Items.Count ?? 0;
        if (Items.Count != outputCount + inputCount) return null;

        int index = (outputSide ? Group.Value.Output.Value?.Items : Group.Value.Input.Value?.Items)?.IndexOf(source) ?? -1;
        return index < 0 ? null : Items[(outputSide ? 0 : outputCount) + index];
    }

    private void OnGroupEdited(object? sender, EventArgs e)
    {
        RaiseEdited();
    }

    [NotAutoSerialized]
    [SuppressResourceClassGeneration]
    public IProperty<GraphGroup> Group { get; } = new ValueOnlyProperty<GraphGroup>(null!) { AffectsRender = false };

    protected override void OnAttachedToHierarchy(in HierarchyAttachmentEventArgs args)
    {
        base.OnAttachedToHierarchy(args);
        Observable.FromEventPattern<PropertyValueChangedEventArgs<GroupOutput?>>(
                h => Group.Value.Output.ValueChanged += h, h => Group.Value.Output.ValueChanged -= h)
            .Subscribe(e => OnOutputChanged(e.EventArgs.NewValue, e.EventArgs.OldValue))
            .DisposeWith(_disposables);

        Observable.FromEventPattern<PropertyValueChangedEventArgs<GroupInput?>>(
                h => Group.Value.Input.ValueChanged += h, h => Group.Value.Input.ValueChanged -= h)
            .Subscribe(e => OnInputChanged(e.EventArgs.NewValue, e.EventArgs.OldValue))
            .DisposeWith(_disposables);
    }

    protected override void OnDetachedFromHierarchy(in HierarchyAttachmentEventArgs args)
    {
        base.OnDetachedFromHierarchy(args);

        _disposables.Clear();
    }

    private void OnOutputChanged(GroupOutput? newObj, GroupOutput? oldObj)
    {
        if (oldObj != null)
        {
            oldObj.Items.CollectionChanged -= OutputItemsCollectionChanged;
            if (!HistoryReplay.IsActive)
                Items.RemoveRange(0, oldObj.Items.Count);
        }

        if (newObj != null)
        {
            newObj.Items.CollectionChanged += OutputItemsCollectionChanged;

            if (!HistoryReplay.IsActive)
            {
                for (int i = 0; i < newObj.Items.Count; i++)
                    AddOutput(i, (IInputPort)newObj.Items[i]);
            }
        }

        SynchronizePortSubscriptions(newObj, _outputPortSubscriptions, outputSide: true);
    }

    private void AddOutput(int index, IInputPort item)
    {
        IOutputPort? outputNodePort = CreateOutput(item.Name.Value, item.AssociatedType!, item.Display.Value);
        Items.Insert(index, outputNodePort);
    }

    private void OutputItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        void Add(int index, IList items)
        {
            foreach (IInputPort item in items)
            {
                AddOutput(index++, item);
            }
        }

        void Remove(int index, IList items)
        {
            Items.RemoveRange(index, items.Count);
        }

        void Reset()
        {
            // Reset is raised after the source list has already changed.
            int outputNodePortCount = _outputPortSubscriptions.Count;
            Items.RemoveRange(0, outputNodePortCount);

            if (Group.Value.Output.Value is { } output)
            {
                for (int i = 0; i < output.Items.Count; i++)
                {
                    AddOutput(i, (IInputPort)output.Items[i]);
                }
            }
        }

        if (!HistoryReplay.IsActive)
            ApplyItemsCollectionChange(e, Add, Remove, Reset);
        SynchronizePortSubscriptions(Group.Value.Output.Value, _outputPortSubscriptions, outputSide: true);
    }

    private void OnInputChanged(GroupInput? newObj, GroupInput? oldObj)
    {
        if (oldObj != null)
        {
            oldObj.Items.CollectionChanged -= InputItemsCollectionChanged;

            if (!HistoryReplay.IsActive)
                Items.RemoveRange(Group.Value.Output.Value?.Items.Count ?? 0, oldObj.Items.Count);
        }

        if (newObj != null)
        {
            newObj.Items.CollectionChanged += InputItemsCollectionChanged;

            if (!HistoryReplay.IsActive)
            {
                for (int i = 0; i < newObj.Items.Count; i++)
                    AddInput(i, (IGroupPort)newObj.Items[i]);
            }
        }

        SynchronizePortSubscriptions(newObj, _inputPortSubscriptions, outputSide: false);
    }

    private void AddInput(int index, IGroupPort item)
    {
        var inputNodePort = CreateInput(item.Name.Value, item.AssociatedType!, item.Display.Value);
        // itemの接続先からデフォルトの値を取ってくる
        var originalInputPort = ((IOutputPort)item).Connections.FirstOrDefault().Value?.Input.Value.Value;
        if (originalInputPort is IInputPort { Property: { } inputProperty })
        {
            object? value = inputProperty.GetValue();
            inputNodePort.Property?.SetValue(value);
        }

        var outputNodePortCount = Group.Value.Output.Value?.Items.Count ?? 0;
        Items.Insert(outputNodePortCount + index, inputNodePort);
    }

    private void InputItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        void Add(int index, IList items)
        {
            foreach (IGroupPort item in items)
            {
                AddInput(index++, item);
            }
        }

        void Remove(int index, IList items)
        {
            var outputNodePortCount = Group.Value.Output.Value?.Items.Count ?? 0;
            Items.RemoveRange(outputNodePortCount + index, items.Count);
        }

        void Reset()
        {
            int outputNodePortCount = _outputPortSubscriptions.Count;
            int inputNodePortCount = _inputPortSubscriptions.Count;
            Items.RemoveRange(outputNodePortCount, inputNodePortCount);

            if (Group.Value.Input.Value is { } input)
            {
                for (int i = 0; i < input.Items.Count; i++)
                {
                    AddInput(i, (IGroupPort)input.Items[i]);
                }
            }
        }

        if (!HistoryReplay.IsActive)
            ApplyItemsCollectionChange(e, Add, Remove, Reset);
        SynchronizePortSubscriptions(Group.Value.Input.Value, _inputPortSubscriptions, outputSide: false);
    }

    // Move and Replace remove before adding: Items is one flat list indexed by a running port
    // count, so inserting first would shift the indices the removal still needs.
    private static void ApplyItemsCollectionChange(
        NotifyCollectionChangedEventArgs e,
        Action<int, IList> add,
        Action<int, IList> remove,
        Action reset)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                add(e.NewStartingIndex, e.NewItems!);
                break;

            case NotifyCollectionChangedAction.Move:
            case NotifyCollectionChangedAction.Replace:
                remove(e.OldStartingIndex, e.OldItems!);
                add(e.NewStartingIndex, e.NewItems!);
                break;

            case NotifyCollectionChangedAction.Remove:
                remove(e.OldStartingIndex, e.OldItems!);
                break;

            case NotifyCollectionChangedAction.Reset:
                reset();
                break;
        }
    }

    public override void Serialize(ICoreSerializationContext context)
    {
        base.Serialize(context);
        context.SetValue("node-tree", Group.Value);
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);

        context.Populate("node-tree", Group.Value);

        OnOutputChanged(Group.Value.Output.Value, null);
        OnInputChanged(Group.Value.Input.Value, null);

        if (context.GetValue<JsonArray>("Items") is { } itemsArray)
        {
            int index = 0;
            foreach (JsonObject itemJson in itemsArray.OfType<JsonObject>())
            {
                if (index < Items.Count)
                {
                    INodeMember nodeMember = Items[index];
                    CoreSerializer.PopulateFromJsonObject(nodeMember, itemJson);
                }

                index++;
            }
        }
    }

    public partial class Resource
    {
        private GraphSnapshot? _innerSnapshot;

        /// <summary>The group's own graph as last evaluated, for reading the nodes inside it.</summary>
        internal GraphSnapshot? InnerSnapshot => _innerSnapshot;

        public override void Initialize(GraphCompositionContext context)
        {
            var node = RequireOriginal();
            node.Group.Value.TopologyChanged += OnGroupTopologyChanged;
            _innerSnapshot = new GraphSnapshot();
            _innerSnapshot.Build(node.Group.Value, context);
        }

        private void OnGroupTopologyChanged(object? sender, EventArgs e)
        {
            _innerSnapshot?.MarkDirty();
        }

        public override void Uninitialize()
        {
            var node = RequireOriginal();
            node.Group.Value.TopologyChanged -= OnGroupTopologyChanged;
            _innerSnapshot?.Dispose();
            _innerSnapshot = null;
        }

        public override void Update(GraphCompositionContext context)
        {
            var node = RequireOriginal();
            if (_innerSnapshot == null) return;

            // Internal edits can change both the port maps and the topological slot order while
            // the outer snapshot remains alive. Build is a no-op when the snapshot is current.
            _innerSnapshot.Build(node.Group.Value, context);
            int groupInputSlotIndex = _innerSnapshot.FindSlotIndex(node.Group.Value.Input.Value);
            int groupOutputSlotIndex = _innerSnapshot.FindSlotIndex(node.Group.Value.Output.Value);

            if (groupInputSlotIndex >= 0)
            {
                if (_innerSnapshot.GetResource(groupInputSlotIndex) is GroupInput.Resource groupInputResource)
                {
                    var outputNodePortCount = node.Group.Value.Output.Value?.Items.Count ?? 0;
                    var inputCount = node.Items.Count - outputNodePortCount;
                    if (inputCount > 0)
                    {
                        var outerValues = new IItemValue[inputCount];
                        for (int i = 0; i < inputCount; i++)
                        {
                            outerValues[i] = ItemValues[outputNodePortCount + i];
                        }

                        groupInputResource.OuterInputValues = outerValues;
                    }
                }
            }

            _innerSnapshot.Evaluate(context);

            var outCount = node.Group.Value.Output.Value?.Items.Count ?? 0;
            if (groupOutputSlotIndex >= 0)
            {
                for (int i = 0; i < outCount; i++)
                {
                    IItemValue? innerValue = _innerSnapshot.GetItemValue(groupOutputSlotIndex, i);
                    if (innerValue != null)
                    {
                        ItemValues[i].PropagateFrom(innerValue);
                    }
                }
            }
        }

        partial void PostDispose(bool disposing)
        {
            if (disposing)
            {
                Uninitialize();
            }
        }
    }
}
