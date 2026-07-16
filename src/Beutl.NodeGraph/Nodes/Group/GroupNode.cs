using System.Collections;
using System.Collections.Specialized;
using System.Reactive.Disposables;
using System.Text.Json.Nodes;
using Beutl.Editor;
using Beutl.NodeGraph.Composition;
using Beutl.Reactive;
using Beutl.Serialization;

namespace Beutl.NodeGraph.Nodes.Group;

// Todo: ファイルからノードグループを読み込めるようにする。
public partial class GroupNode : GraphNode
{
    public static readonly CoreProperty<GraphGroup> GroupProperty;
    private readonly CompositeDisposable _disposables = [];
    private readonly List<IDisposable> _outputNodePortDisposable = [];
    private readonly List<IDisposable> _inputNodePortDisposable = [];

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

    private void DisposeOutput()
    {
        foreach (IDisposable item in _outputNodePortDisposable)
        {
            item.Dispose();
        }

        _outputNodePortDisposable.Clear();
    }

    private void DisposeInput()
    {
        foreach (IDisposable item in _inputNodePortDisposable)
        {
            item.Dispose();
        }

        _inputNodePortDisposable.Clear();
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
        if (RecordingSuppression.IsSuppressed) return;
        if (oldObj != null)
        {
            oldObj.Items.CollectionChanged -= OutputItemsCollectionChanged;
            var outputNodePortCount = oldObj.Items.Count;
            Items.RemoveRange(0, outputNodePortCount);
            DisposeOutput();
        }

        if (newObj != null)
        {
            newObj.Items.CollectionChanged += OutputItemsCollectionChanged;

            for (int i = 0; i < newObj.Items.Count; i++)
            {
                IInputPort item = (IInputPort)newObj.Items[i];
                AddOutput(i, item);
            }
        }
    }

    private void AddOutput(int index, IInputPort item)
    {
        IOutputPort? outputNodePort = CreateOutput(item.Name, item.AssociatedType!);

        _outputNodePortDisposable.Insert(index, ((CoreObject)item).GetObservable(NameProperty)
            .Subscribe(v => outputNodePort.Name = v));
        Items.Insert(index, outputNodePort);
    }

    private void OutputItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (RecordingSuppression.IsSuppressed) return;

        void Add(int index, IList items)
        {
            foreach (IInputPort item in items)
            {
                AddOutput(index++, item);
            }
        }

        void Remove(int index, IList items)
        {
            for (int i = index; i < items.Count; i++)
            {
                _outputNodePortDisposable[i].Dispose();
            }

            _outputNodePortDisposable.RemoveRange(index, items.Count);

            Items.RemoveRange(index, items.Count);
        }

        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                Add(e.NewStartingIndex, e.NewItems!);
                break;

            case NotifyCollectionChangedAction.Move:
            case NotifyCollectionChangedAction.Replace:
                Remove(e.OldStartingIndex, e.OldItems!);
                Add(e.NewStartingIndex, e.NewItems!);
                break;

            case NotifyCollectionChangedAction.Remove:
                Remove(e.OldStartingIndex, e.OldItems!);
                break;

            case NotifyCollectionChangedAction.Reset:
                var outputNodePortCount = Group.Output?.Items.Count ?? 0;
                Items.RemoveRange(0, outputNodePortCount);
                // _outputNodePortCount = 0;
                break;
        }
    }

    private void OnInputChanged(GroupInput? newObj, GroupInput? oldObj)
    {
        if (RecordingSuppression.IsSuppressed) return;
        if (oldObj != null)
        {
            oldObj.Items.CollectionChanged -= InputItemsCollectionChanged;

            var outputNodePortCount = Group.Output?.Items.Count ?? 0;
            var inputNodePortCount = oldObj.Items.Count;
            Items.RemoveRange(outputNodePortCount, inputNodePortCount);
            DisposeInput();
        }

        if (newObj != null)
        {
            newObj.Items.CollectionChanged += InputItemsCollectionChanged;

            for (int i = 0; i < newObj.Items.Count; i++)
            {
                IGroupPort item = (IGroupPort)newObj.Items[i];
                AddInput(i, item);
            }
        }
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

        _inputNodePortDisposable.Insert(index, ((CoreObject)item).GetObservable(NameProperty)
            .Subscribe(v => inputNodePort.Name = v));
        var outputNodePortCount = Group.Output?.Items.Count ?? 0;
        Items.Insert(outputNodePortCount + index, inputNodePort);
    }

    private void InputItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (RecordingSuppression.IsSuppressed) return;

        void Add(int index, IList items)
        {
            foreach (IGroupPort item in items)
            {
                AddInput(index++, item);
            }
        }

        void Remove(int index, IList items)
        {
            for (int i = index; i < items.Count; i++)
            {
                _inputNodePortDisposable[i].Dispose();
            }

            _inputNodePortDisposable.RemoveRange(index, items.Count);


            var outputNodePortCount = Group.Output?.Items.Count ?? 0;
            Items.RemoveRange(outputNodePortCount + index, items.Count);
        }

        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                Add(e.NewStartingIndex, e.NewItems!);
                break;

            case NotifyCollectionChangedAction.Move:
            case NotifyCollectionChangedAction.Replace:
                Remove(e.OldStartingIndex, e.OldItems!);
                Add(e.NewStartingIndex, e.NewItems!);
                break;

            case NotifyCollectionChangedAction.Remove:
                Remove(e.OldStartingIndex, e.OldItems!);
                break;

            case NotifyCollectionChangedAction.Reset:
                var outputNodePortCount = Group.Output?.Items.Count ?? 0;
                var inputNodePortCount = Group.Input?.Items.Count ?? 0;
                Items.RemoveRange(outputNodePortCount, inputNodePortCount);
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
        private GraphGroup? _subscribedGroup;
        private int _groupInputSlotIndex = -1;
        private int _groupOutputSlotIndex = -1;
        private bool _innerResourcesReservedForDispose;
        private GeneratedResourceCleanupContext? _resourceCleanupContext;

        protected override void InitializeCore(GraphCompositionContext context)
        {
            var node = GetOriginal();
            GraphGroup group = node.Group;
            group.TopologyChanged += OnGroupTopologyChanged;
            _subscribedGroup = group;
            _innerSnapshot = new GraphSnapshot();
            _innerSnapshot.Build(group, context);
            _groupInputSlotIndex = _innerSnapshot.FindSlotIndex(group.Input);
            _groupOutputSlotIndex = _innerSnapshot.FindSlotIndex(group.Output);
        }

        private void OnGroupTopologyChanged(object? sender, EventArgs e)
        {
            _innerSnapshot?.MarkDirty();
        }

        protected override void UninitializeCore()
        {
            Uninitialize(_innerResourcesReservedForDispose);
        }

        private void Uninitialize(bool resourcesReserved)
        {
            _innerResourcesReservedForDispose = false;
            GraphSnapshot? innerSnapshot = _innerSnapshot;
            Exception? failure = null;
            bool ownershipDetached = innerSnapshot == null;
            if (resourcesReserved && innerSnapshot != null)
            {
                GeneratedResourceCleanupContext? context = _resourceCleanupContext;
                if (context == null)
                {
                    failure = new InvalidOperationException(
                        "The inner graph resources were not reserved before cleanup.");
                    innerSnapshot.RollbackCleanupReservation();
                    _ = innerSnapshot.DetachAndDisposeWithoutReservation();
                    ownershipDetached = !innerSnapshot.HasOwnedState();
                }
                else
                {
                    try
                    {
                        innerSnapshot.DisposeAfterResourcesReserved(context.DisposeOwned, context.Capture);
                        ownershipDetached = true;
                    }
                    catch (Exception ex)
                    {
                        failure = ex;
                        ownershipDetached = !innerSnapshot.HasOwnedState();
                    }
                }
            }
            else if (innerSnapshot != null)
            {
                try
                {
                    innerSnapshot.Dispose();
                    ownershipDetached = true;
                }
                catch (Exception ex)
                {
                    failure = ex;
                    ownershipDetached = !innerSnapshot.HasOwnedState();
                }
            }

            if (ownershipDetached)
            {
                _innerSnapshot = null;
                _groupInputSlotIndex = -1;
                _groupOutputSlotIndex = -1;

                GraphGroup? subscribedGroup = _subscribedGroup;
                _subscribedGroup = null;
                subscribedGroup?.TopologyChanged -= OnGroupTopologyChanged;
            }

            if (resourcesReserved)
                _resourceCleanupContext = null;

            ThrowIfCleanupFailed(failure);
        }

        protected override void UpdateCore(GraphCompositionContext context)
        {
            var node = GetOriginal();
            if (_innerSnapshot == null) return;

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

        partial void PrepareResourceDispose(bool disposing, GeneratedResourceCleanupContext context)
        {
            if (disposing)
            {
                _resourceCleanupContext = context;
                _innerSnapshot?.ReserveResources(context.Reserve);
                _innerResourcesReservedForDispose = true;
            }
        }

        partial void RollbackResourceDisposePreparation()
        {
            _innerSnapshot?.RollbackCleanupReservation();
            _innerResourcesReservedForDispose = false;
            _resourceCleanupContext = null;
        }

        partial void PostDispose(bool disposing)
        {
            if (disposing)
            {
                Uninitialize(resourcesReserved: true);
            }
        }
    }
}
