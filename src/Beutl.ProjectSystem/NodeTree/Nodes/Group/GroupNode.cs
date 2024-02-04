using System.Collections;
using System.Collections.Specialized;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text.Json.Nodes;

using Beutl.Reactive;
using Beutl.Serialization;

namespace Beutl.NodeTree.Nodes.Group;

// Todo: ファイルからノードグループを読み込めるようにする。
public class GroupNode : Node
{
    private readonly CompositeDisposable _disposables = [];
    private readonly List<IDisposable> _outputSocketDisposable = [];
    private readonly List<IDisposable> _inputSocketDisposable = [];
    private int _outputSocketCount = 0;
    private int _inputSocketCount = 0;

    public GroupNode()
    {
        Group = new NodeGroup()
        {
            Name = "Group"
        };
        HierarchicalChildren.Add(Group);
        Group.Invalidated += OnGroupInvalidated;

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

    private void OnGroupInvalidated(object? sender, Media.RenderInvalidatedEventArgs e)
    {
        RaiseInvalidated(e);
    }

    public NodeGroup Group { get; }

    public override void InitializeForContext(NodeEvaluationContext context)
    {
        base.InitializeForContext(context);
        context.State = Group.InitializeForState(context.Renderer, context.Clock);
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
        for (int i = _outputSocketCount; i < Items.Count; i++)
        {
            INodeItem input = Items[i];
            INodeItem? output = Group.Input?.Items[i - _outputSocketCount];
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
        for (int i = 0; i < _outputSocketCount; i++)
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
        if (oldObj != null)
        {
            oldObj.Items.CollectionChanged -= OutputItemsCollectionChanged;
            Items.RemoveRange(0, _outputSocketCount);
            DisposeOutput();
            _outputSocketCount = 0;
        }

        if (newObj != null)
        {
            newObj.Items.CollectionChanged += OutputItemsCollectionChanged;

            for (int i = 0; i < newObj.Items.Count; i++)
            {
                IInputSocket item = (IInputSocket)newObj.Items[i];
                AddOutput(i, item);
            }

            _outputSocketCount = newObj.Items.Count;
        }
    }

    private void AddOutput(int index, IInputSocket item)
    {
        IOutputSocket? outputSocket = CreateOutput(NodeDisplayNameHelper.GetDisplayName(item), item.AssociatedType!);
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
        void Add(int index, IList items)
        {
            foreach (IInputSocket item in items)
            {
                AddOutput(index++, item);
                _outputSocketCount++;
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
            _outputSocketCount -= items.Count;
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
                Items.RemoveRange(0, _outputSocketCount);
                _outputSocketCount = 0;
                break;
        }
    }

    private void OnInputChanged(GroupInput? newObj, GroupInput? oldObj)
    {
        if (oldObj != null)
        {
            oldObj.Items.CollectionChanged -= InputItemsCollectionChanged;
            Items.RemoveRange(_outputSocketCount, _inputSocketCount);
            DisposeInput();
            _inputSocketCount = 0;
        }

        if (newObj != null)
        {
            newObj.Items.CollectionChanged += InputItemsCollectionChanged;

            for (int i = 0; i < newObj.Items.Count; i++)
            {
                IGroupSocket item = (IGroupSocket)newObj.Items[i];
                AddInput(i, item);
            }

            _inputSocketCount = newObj.Items.Count;
        }
    }

    private void AddInput(int index, IGroupSocket item)
    {
        IInputSocket? inputSocket;
        if (item.AssociatedProperty != null)
        {
            inputSocket = CreateInput(item.AssociatedProperty);
            inputSocket.Property?.SetValue(item.Property?.GetValue());
            inputSocket.Name = item.Name;
        }
        else
        {
            inputSocket = CreateInput(NodeDisplayNameHelper.GetDisplayName(item), item.AssociatedType!);
        }

        _inputSocketDisposable.Insert(index, ((CoreObject)item).GetObservable(NameProperty)
            .Subscribe(v => inputSocket.Name = v));
        Items.Insert(_outputSocketCount + index, inputSocket);
    }

    private void InputItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        void Add(int index, IList items)
        {
            foreach (IGroupSocket item in items)
            {
                AddInput(index++, item);
                _inputSocketCount++;
            }
        }

        void Remove(int index, IList items)
        {
            for (int i = index; i < items.Count; i++)
            {
                _inputSocketDisposable[i].Dispose();
            }

            _inputSocketDisposable.RemoveRange(index, items.Count);

            Items.RemoveRange(_outputSocketCount + index, items.Count);
            _inputSocketCount -= items.Count;
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
                Items.RemoveRange(_outputSocketCount, _inputSocketCount);
                _inputSocketCount = 0;
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
                    INodeItem? nodeItem = Items[index];
                    if (LocalSerializationErrorNotifier.Current is not { } notifier)
                    {
                        notifier = NullSerializationErrorNotifier.Instance;
                    }
                    ICoreSerializationContext? parent = ThreadLocalSerializationContext.Current;

                    var innerContext = new JsonSerializationContext(nodeItem.GetType(), notifier, parent, itemJson);
                    using (ThreadLocalSerializationContext.Enter(innerContext))
                    {
                        nodeItem.Deserialize(innerContext);
                    }

                    ((NodeItem)nodeItem).LocalId = index;
                }

                index++;
            }

            NextLocalId = index;
        }
    }
}
