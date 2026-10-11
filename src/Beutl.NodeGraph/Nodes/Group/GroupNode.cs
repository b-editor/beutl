using System.Collections;
using System.Collections.Specialized;
using System.Reactive.Disposables;
using System.Text.Json.Nodes;
using Beutl.Editor;
using Beutl.NodeGraph.Composition;
using Beutl.Reactive;
using Beutl.Serialization;

namespace Beutl.NodeGraph.Nodes.Group;

public partial class GroupNode : GraphNode
{
    public static readonly CoreProperty<GraphGroup> GroupProperty;
    private readonly CompositeDisposable _disposables = [];
    private readonly Dictionary<INodeMember, IDisposable> _outputPortSubscriptions = [];
    private readonly Dictionary<INodeMember, IDisposable> _inputPortSubscriptions = [];

    static GroupNode()
    {
        GroupProperty = ConfigureProperty<GraphGroup, GroupNode>(nameof(Group))
            .Accessor(o => o.Group)
            .Register();
    }

    public GroupNode()
    {
        Group = new GraphGroup() { Name = "Group" };
        HierarchicalChildren.Add(Group);
        Group.Edited += OnGroupEdited;

        this.GetObservable(NameProperty).Subscribe(v => Group.Name = string.IsNullOrWhiteSpace(v) ? "Group" : v);
        Group.GetObservable(NameProperty).Subscribe(v => Name = v == "Group" ? "" : v);
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
        ((CoreObject)source).GetPropertyChangedObservable(NameProperty)
            .Subscribe(_ =>
            {
                if (FindMirror(source, outputSide) is { } mirror) mirror.Name = source.Name;
            }).DisposeWith(subscriptions);
        ((NodeMember)source).GetPropertyChangedObservable(NodeMember.DisplayProperty)
            .Subscribe(e =>
            {
                if (FindMirror(source, outputSide) is NodeMember mirror) mirror.Display = e.NewValue;
            }).DisposeWith(subscriptions);
        return subscriptions;
    }

    private INodeMember? FindMirror(INodeMember source, bool outputSide)
    {
        int outputCount = Group.Output?.Items.Count ?? 0;
        int inputCount = Group.Input?.Items.Count ?? 0;
        if (Items.Count != outputCount + inputCount) return null;

        int index = (outputSide ? Group.Output?.Items : Group.Input?.Items)?.IndexOf(source) ?? -1;
        return index < 0 ? null : Items[(outputSide ? 0 : outputCount) + index];
    }

    private void OnGroupEdited(object? sender, EventArgs e)
    {
        RaiseEdited();
    }

    public GraphGroup Group { get; }

    protected override void OnAttachedToHierarchy(in HierarchyAttachmentEventArgs args)
    {
        base.OnAttachedToHierarchy(args);
        Group.GetPropertyChangedObservable(GraphGroup.OutputProperty)
            .Subscribe(e => OnOutputChanged(e.NewValue, e.OldValue))
            .DisposeWith(_disposables);

        Group.GetPropertyChangedObservable(GraphGroup.InputProperty)
            .Subscribe(e => OnInputChanged(e.NewValue, e.OldValue))
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
            if (!RecordingSuppression.IsSuppressed)
                Items.RemoveRange(0, oldObj.Items.Count);
        }

        if (newObj != null)
        {
            newObj.Items.CollectionChanged += OutputItemsCollectionChanged;

            if (!RecordingSuppression.IsSuppressed)
            {
                for (int i = 0; i < newObj.Items.Count; i++)
                    AddOutput(i, (IInputPort)newObj.Items[i]);
            }
        }

        SynchronizePortSubscriptions(newObj, _outputPortSubscriptions, outputSide: true);
    }

    private void AddOutput(int index, IInputPort item)
    {
        IOutputPort? outputNodePort = CreateOutput(item.Name, item.AssociatedType!, item.Display);
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

            if (Group.Output is { } output)
            {
                for (int i = 0; i < output.Items.Count; i++)
                {
                    AddOutput(i, (IInputPort)output.Items[i]);
                }
            }
        }

        if (!RecordingSuppression.IsSuppressed)
            ApplyItemsCollectionChange(e, Add, Remove, Reset);
        SynchronizePortSubscriptions(Group.Output, _outputPortSubscriptions, outputSide: true);
    }

    private void OnInputChanged(GroupInput? newObj, GroupInput? oldObj)
    {
        if (oldObj != null)
        {
            oldObj.Items.CollectionChanged -= InputItemsCollectionChanged;

            if (!RecordingSuppression.IsSuppressed)
                Items.RemoveRange(Group.Output?.Items.Count ?? 0, oldObj.Items.Count);
        }

        if (newObj != null)
        {
            newObj.Items.CollectionChanged += InputItemsCollectionChanged;

            if (!RecordingSuppression.IsSuppressed)
            {
                for (int i = 0; i < newObj.Items.Count; i++)
                    AddInput(i, (IGroupPort)newObj.Items[i]);
            }
        }

        SynchronizePortSubscriptions(newObj, _inputPortSubscriptions, outputSide: false);
    }

    private void AddInput(int index, IGroupPort item)
    {
        var inputNodePort = CreateInput(item.Name, item.AssociatedType!, item.Display);
        // itemの接続先からデフォルトの値を取ってくる
        var originalInputPort = ((IOutputPort)item).Connections.FirstOrDefault().Value?.Input.Value;
        if (originalInputPort is IInputPort { Property: { } inputProperty })
        {
            object? value = inputProperty.GetValue();
            inputNodePort.Property?.SetValue(value);
        }

        var outputNodePortCount = Group.Output?.Items.Count ?? 0;
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
            var outputNodePortCount = Group.Output?.Items.Count ?? 0;
            Items.RemoveRange(outputNodePortCount + index, items.Count);
        }

        void Reset()
        {
            int outputNodePortCount = _outputPortSubscriptions.Count;
            int inputNodePortCount = _inputPortSubscriptions.Count;
            Items.RemoveRange(outputNodePortCount, inputNodePortCount);

            if (Group.Input is { } input)
            {
                for (int i = 0; i < input.Items.Count; i++)
                {
                    AddInput(i, (IGroupPort)input.Items[i]);
                }
            }
        }

        if (!RecordingSuppression.IsSuppressed)
            ApplyItemsCollectionChange(e, Add, Remove, Reset);
        SynchronizePortSubscriptions(Group.Input, _inputPortSubscriptions, outputSide: false);
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
        context.SetValue("node-tree", Group);
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);

        context.Populate("node-tree", Group);

        OnOutputChanged(Group.Output, null);
        OnInputChanged(Group.Input, null);

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
        private int _groupInputSlotIndex = -1;
        private int _groupOutputSlotIndex = -1;

        /// <summary>The group's own graph as last evaluated, for reading the nodes inside it.</summary>
        internal GraphSnapshot? InnerSnapshot => _innerSnapshot;

        public override void Initialize(GraphCompositionContext context)
        {
            var node = RequireOriginal();
            node.Group.TopologyChanged += OnGroupTopologyChanged;
            _innerSnapshot = new GraphSnapshot();
            _innerSnapshot.Build(node.Group, context);
            _groupInputSlotIndex = _innerSnapshot.FindSlotIndex(node.Group.Input);
            _groupOutputSlotIndex = _innerSnapshot.FindSlotIndex(node.Group.Output);
        }

        private void OnGroupTopologyChanged(object? sender, EventArgs e)
        {
            _innerSnapshot?.MarkDirty();
        }

        public override void Uninitialize()
        {
            var node = RequireOriginal();
            node.Group.TopologyChanged -= OnGroupTopologyChanged;
            _innerSnapshot?.Dispose();
            _innerSnapshot = null;
            _groupInputSlotIndex = -1;
            _groupOutputSlotIndex = -1;
        }

        public override void Update(GraphCompositionContext context)
        {
            var node = RequireOriginal();
            if (_innerSnapshot == null) return;

            // Internal edits can change both the port maps and the topological slot order while
            // the outer snapshot remains alive. Build is a no-op when the snapshot is current.
            _innerSnapshot.Build(node.Group, context);
            _groupInputSlotIndex = _innerSnapshot.FindSlotIndex(node.Group.Input);
            _groupOutputSlotIndex = _innerSnapshot.FindSlotIndex(node.Group.Output);

            // GroupNodeの入力値からGroupInputの出力値に転送
            if (node.Group.Input != null && _groupInputSlotIndex >= 0)
            {
                if (_innerSnapshot.GetResource(_groupInputSlotIndex) is GroupInput.Resource groupInputResource)
                {
                    var outputNodePortCount = node.Group.Output?.Items.Count ?? 0;
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

            // 内部スナップショットを評価
            _innerSnapshot.Evaluate(context.Target, context);

            // GroupOutputの入力値からGroupNodeの出力値に転送
            var outCount = node.Group.Output?.Items.Count ?? 0;
            if (_groupOutputSlotIndex >= 0)
            {
                for (int i = 0; i < outCount; i++)
                {
                    IItemValue? innerValue = _innerSnapshot.GetItemValue(_groupOutputSlotIndex, i);
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
