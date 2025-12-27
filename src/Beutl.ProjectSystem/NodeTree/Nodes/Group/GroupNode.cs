using System.Collections;
using System.Collections.Specialized;
using System.Reactive.Disposables;
using System.Text.Json.Nodes;
using Beutl.Editor;
using Beutl.Reactive;
using Beutl.Serialization;

namespace Beutl.NodeTree.Nodes.Group;

// Todo: ファイルからノードグループを読み込めるようにする。
public class GroupNode : Node
{
    public static readonly CoreProperty<NodeGroup> GroupProperty;
    private readonly CompositeDisposable _disposables = [];
    private readonly List<IDisposable> _outputSocketDisposable = [];
    private readonly List<IDisposable> _inputSocketDisposable = [];

    static GroupNode()
    {
        GroupProperty = ConfigureProperty<NodeGroup, GroupNode>(nameof(Group))
            .Accessor(o => o.Group)
            .Register();
    }

    public GroupNode()
    {
        Group = new NodeGroup() { Name = "Group" };
        HierarchicalChildren.Add(Group);
        Group.Edited += OnGroupEdited;

        this.GetObservable(NameProperty).Subscribe(v => Group.Name = string.IsNullOrWhiteSpace(v) ? "Group" : v);
        Group.GetObservable(NameProperty).Subscribe(v => Name = v == "Group" ? "" : v);
    }

    private void DisposeOutput()
    {
        foreach (IDisposable item in _outputSocketDisposable)
        {
            item.Dispose();
        }

        _outputSocketDisposable.Clear();
    }

    private void DisposeInput()
    {
        foreach (IDisposable item in _inputSocketDisposable)
        {
            item.Dispose();
        }

        _inputSocketDisposable.Clear();
    }

    private void OnGroupEdited(object? sender, EventArgs e)
    {
        RaiseInvalidated(e);
    }

    public NodeGroup Group { get; }

    public override void InitializeForContext(NodeEvaluationContext context)
    {
        base.InitializeForContext(context);
        context.State = Group.InitializeForState(context.Renderer);
    }

    public override void UninitializeForContext(NodeEvaluationContext context)
    {
        base.UninitializeForContext(context);
        Group.UninitializeForState(context.State);
        context.State = null;
    }

    public override void PreEvaluate(NodeEvaluationContext context)
    {
        base.PreEvaluate(context);
        var outputSocketCount = Group.Output?.Items.Count ?? 0;
        for (int i = outputSocketCount; i < Items.Count; i++)
        {
            INodeItem input = Items[i];
            INodeItem? output = Group.Input?.Items[i - outputSocketCount];
            if (input is IInputSocket inputSocket
                && output is ISupportSetValueNodeItem outputSocket)
            {
                outputSocket.SetThrough(inputSocket);
            }
        }
    }

    public override void Evaluate(NodeEvaluationContext context)
    {
        base.Evaluate(context);

        if (context.State is { })
        {
            Group.Evaluate(context, context.State);
        }
    }

    public override void PostEvaluate(NodeEvaluationContext context)
    {
        var outputSocketCount = Group.Output?.Items.Count ?? 0;
        for (int i = 0; i < outputSocketCount; i++)
        {
            INodeItem output = Items[i];
            INodeItem? input = Group.Output?.Items[i];
            if (input is IInputSocket inputSocket
                && output is ISupportSetValueNodeItem outputSocket)
            {
                outputSocket.SetThrough(inputSocket);
            }
        }
        base.PostEvaluate(context);
    }

    protected override void OnAttachedToHierarchy(in HierarchyAttachmentEventArgs args)
    {
        base.OnAttachedToHierarchy(args);
        Group.GetPropertyChangedObservable(NodeGroup.OutputProperty)
            .Subscribe(e => OnOutputChanged(e.NewValue, e.OldValue))
            .DisposeWith(_disposables);

        Group.GetPropertyChangedObservable(NodeGroup.InputProperty)
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
            var outputSocketCount = oldObj.Items.Count;
            Items.RemoveRange(0, outputSocketCount);
            DisposeOutput();
        }

        if (newObj != null)
        {
            newObj.Items.CollectionChanged += OutputItemsCollectionChanged;

            for (int i = 0; i < newObj.Items.Count; i++)
            {
                IInputSocket item = (IInputSocket)newObj.Items[i];
                AddOutput(i, item);
            }
        }
    }

    private void AddOutput(int index, IInputSocket item)
    {
        IOutputSocket? outputSocket = CreateOutput(item.Name, item.AssociatedType!);
        if (outputSocket is ISupportSetValueNodeItem supportSetValue)
        {
            supportSetValue.SetThrough(item);
        }

        _outputSocketDisposable.Insert(index, ((CoreObject)item).GetObservable(NameProperty)
            .Subscribe(v => outputSocket.Name = v));
        Items.Insert(index, outputSocket);
    }

    private void OutputItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (RecordingSuppression.IsSuppressed) return;
        void Add(int index, IList items)
        {
            foreach (IInputSocket item in items)
            {
                AddOutput(index++, item);
            }
        }

        void Remove(int index, IList items)
        {
            for (int i = index; i < items.Count; i++)
            {
                _outputSocketDisposable[i].Dispose();
            }

            _outputSocketDisposable.RemoveRange(index, items.Count);

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
                var outputSocketCount = Group.Output?.Items.Count ?? 0;
                Items.RemoveRange(0, outputSocketCount);
                // _outputSocketCount = 0;
                break;
        }
    }

    private void OnInputChanged(GroupInput? newObj, GroupInput? oldObj)
    {
        if (RecordingSuppression.IsSuppressed) return;
        if (oldObj != null)
        {
            oldObj.Items.CollectionChanged -= InputItemsCollectionChanged;

            var outputSocketCount = Group.Output?.Items.Count ?? 0;
            var inputSocketCount = oldObj.Items.Count;
            Items.RemoveRange(outputSocketCount, inputSocketCount);
            DisposeInput();
        }

        if (newObj != null)
        {
            newObj.Items.CollectionChanged += InputItemsCollectionChanged;

            for (int i = 0; i < newObj.Items.Count; i++)
            {
                IGroupSocket item = (IGroupSocket)newObj.Items[i];
                AddInput(i, item);
            }
        }
    }

    private void AddInput(int index, IGroupSocket item)
    {
        IInputSocket? inputSocket;
        if (item.AssociatedPropertyName != null && item.AssociatedPropertyType != null)
        {
            inputSocket = CreateInput(item.AssociatedPropertyName, item.AssociatedPropertyType);
            inputSocket.Property?.SetValue(item.Property?.GetValue());
            inputSocket.Name = item.Name;
        }
        else
        {
            inputSocket = CreateInput(item.Name, item.AssociatedType!);
        }

        _inputSocketDisposable.Insert(index, ((CoreObject)item).GetObservable(NameProperty)
            .Subscribe(v => inputSocket.Name = v));
        var outputSocketCount = Group.Output?.Items.Count ?? 0;
        Items.Insert(outputSocketCount + index, inputSocket);
    }

    private void InputItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (RecordingSuppression.IsSuppressed) return;
        void Add(int index, IList items)
        {
            foreach (IGroupSocket item in items)
            {
                AddInput(index++, item);
            }
        }

        void Remove(int index, IList items)
        {
            for (int i = index; i < items.Count; i++)
            {
                _inputSocketDisposable[i].Dispose();
            }

            _inputSocketDisposable.RemoveRange(index, items.Count);


            var outputSocketCount = Group.Output?.Items.Count ?? 0;
            Items.RemoveRange(outputSocketCount + index, items.Count);
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
                var outputSocketCount = Group.Output?.Items.Count ?? 0;
                var inputSocketCount = Group.Input?.Items.Count ?? 0;
                Items.RemoveRange(outputSocketCount, inputSocketCount);
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
                    INodeItem nodeItem = Items[index];
                    CoreSerializer.PopulateFromJsonObject(nodeItem, itemJson);
                    ((NodeItem)nodeItem).LocalId = index;
                }

                index++;
            }

            NextLocalId = index;
        }
    }
}
