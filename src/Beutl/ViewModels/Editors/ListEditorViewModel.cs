using System.Collections.Specialized;
using System.Text.Json.Nodes;

using Beutl.Animation;
using Beutl.Services;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;

namespace Beutl.ViewModels.Editors;

public sealed class ListItemAccessorImpl<T> : IAbstractProperty<T>
{
    private readonly ReactivePropertySlim<T?> _inner = new();
    private readonly IList<T?> _list;

    public ListItemAccessorImpl(int index, T? item, IList<T?> list)
    {
        Index = index;
        _inner.Value = item;
        _list = list;
    }

    public Type ImplementedType => throw new InvalidOperationException();

    public Type PropertyType => typeof(T);

    public string DisplayName => "";

    public bool IsReadOnly => false;

    public int Index { get; set; }

    public object? GetDefaultValue()
    {
        return default(T);
    }

    public IObservable<T?> GetObservable()
    {
        return _inner;
    }

    public T? GetValue()
    {
        return _inner.Value;
    }

    public void SetValue(T? value)
    {
        _list[Index] = value;
    }

    public void OnItemChanged(T? value)
    {
        _inner.Value = value;
    }
}

public interface IListItemEditorViewModel : IJsonSerializable
{
    void OnDeleteRequested();
}

public interface IListEditorViewModel
{
    void MoveItem(int oldIndex, int newIndex);
}

public sealed class ListItemEditorViewModel<TItem> : IDisposable, IListItemEditorViewModel
{
    public ListItemEditorViewModel(ListEditorViewModel<TItem> parent, ListItemAccessorImpl<TItem?> itemAccessor)
    {
        Parent = parent;
        ItemAccessor = itemAccessor;

        var tmp = new IAbstractProperty[] { itemAccessor };
        (_, PropertyEditorExtension ext) = PropertyEditorService.MatchProperty(tmp);
        if (ext?.TryCreateContextForListItem(itemAccessor, out IPropertyEditorContext? context) == true)
        {
            Context = context;
        }
    }

    public ListEditorViewModel<TItem> Parent { get; }

    public ListItemAccessorImpl<TItem?> ItemAccessor { get; }

    public IPropertyEditorContext? Context { get; }

    public void Dispose()
    {
        Context?.Dispose();
    }

    public void OnDeleteRequested()
    {
        Parent.RemoveItem(ItemAccessor.Index);
    }

    public void ReadFromJson(JsonObject json)
    {
        Context?.ReadFromJson(json);
    }

    public void WriteToJson(JsonObject json)
    {
        Context?.WriteToJson(json);
    }
}

public sealed class ListEditorViewModel<TItem> : BaseEditorViewModel, IListEditorViewModel
{
    private static readonly NotifyCollectionChangedEventArgs s_resetCollectionChanged = new(NotifyCollectionChangedAction.Reset);
    private INotifyCollectionChanged? _incc;

    public ListEditorViewModel(IAbstractProperty property)
        : base(property)
    {
        List = property.GetObservable()
            .Select(x => x as IList<TItem?>)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        IsExpanded.Skip(1)
            .Take(1)
            .Subscribe(_ =>
                List.Subscribe(list =>
                {
                    if (_incc != null)
                    {
                        _incc.CollectionChanged -= OnCollectionChanged;
                        _incc = null;

                        OnCollectionChanged(s_resetCollectionChanged);
                    }

                    if (list is INotifyCollectionChanged incc)
                    {
                        _incc = incc;
                        _incc.CollectionChanged += OnCollectionChanged;
                        var args = new NotifyCollectionChangedEventArgs(
                            action: NotifyCollectionChangedAction.Add,
                            changedItems: list.ToArray(),
                            startingIndex: 0);
                        OnCollectionChanged(args);
                    }
                })
                .DisposeWith(Disposables))
            .DisposeWith(Disposables);
    }

    public ReadOnlyReactivePropertySlim<IList<TItem?>?> List { get; }

    public CoreList<ListItemEditorViewModel<TItem>> Items { get; } = [];

    public ReactivePropertySlim<bool> IsExpanded { get; } = new(false);

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnCollectionChanged(e);
    }

    private void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        void Added(int index, TItem? obj)
        {
            var visitor = new Visitor(this);
            var itemAccessor = new ListItemAccessorImpl<TItem?>(index, obj, List.Value!);
            var item = new ListItemEditorViewModel<TItem>(this, itemAccessor);

            item.Context?.Accept(visitor);
            Items.Insert(index, item);
        }

        void Removed(int index)
        {
            ListItemEditorViewModel<TItem> item = Items[index];
            if (this.GetService<ISupportCloseAnimation>() is { } service
                && item.ItemAccessor.GetValue() is IAnimatable animatable)
            {
                service.Close(animatable);
            }

            Items.RemoveAt(index);
            item.Dispose();
        }

        void UpdateIndex(int start)
        {
            for (int i = start; i < Items.Count; i++)
            {
                Items[i].ItemAccessor.Index = i;
            }
        }

        int index;
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                index = e.NewStartingIndex;
                foreach (TItem? item in e.NewItems!)
                {
                    Added(index++, item);
                }

                UpdateIndex(index + 1);
                break;

            case NotifyCollectionChangedAction.Remove:
                index = e.OldStartingIndex;
                for (int i = e.OldItems!.Count - 1; i >= 0; --i)
                {
                    Removed(index + i);
                }

                UpdateIndex(index);
                break;

            case NotifyCollectionChangedAction.Replace:
                index = e.NewStartingIndex;
                for (int i = 0; i < e.NewItems!.Count; i++)
                {
                    Items[index].ItemAccessor.OnItemChanged(List.Value![index]);
                    index++;
                }
                break;

            case NotifyCollectionChangedAction.Move:
                int newIndex = e.NewStartingIndex;
                if (newIndex > e.OldStartingIndex)
                {
                    newIndex += e.OldItems!.Count;
                }
                Items.MoveRange(e.OldStartingIndex, e.NewItems!.Count, newIndex);

                UpdateIndex(0);
                break;

            case NotifyCollectionChangedAction.Reset:
                for (int i = Items.Count - 1; i >= 0; i--)
                {
                    ListItemEditorViewModel<TItem> item = Items[i];
                    Items.RemoveAt(i);
                    item.Dispose();
                }
                break;
        }
    }

    public void Initialize()
    {
        if (List.Value == null)
        {
            Type listType = WrappedProperty.PropertyType;
            if (WrappedProperty.IsReadOnly)
                throw new InvalidOperationException("読み取り専用です。");

            if (listType.IsInterface || listType.IsAbstract)
                throw new InvalidOperationException("抽象型を初期化できません。");

            var list = Activator.CreateInstance(listType) as IList<TItem>;
            var command = new SetCommand(WrappedProperty, null, list);
            command.DoAndRecord(CommandRecorder.Default);
        }
        else
        {
            List.Value.Clear();
        }
    }

    public void Delete()
    {
        if (List.Value != null)
        {
            if (WrappedProperty.IsReadOnly)
                throw new InvalidOperationException("読み取り専用です。");

            var command = new SetCommand(WrappedProperty, List.Value, null);
            command.DoAndRecord(CommandRecorder.Default);
        }
    }

    public override void Reset()
    {
        try
        {
            Initialize();
        }
        catch (InvalidOperationException)
        {
        }
    }

    public void AddItem(TItem? item)
    {
        List.Value!.BeginRecord()
            .Add(item)
            .ToCommand()
            .DoAndRecord(CommandRecorder.Default);
    }

    public void RemoveItem(int index)
    {
        List.Value!.BeginRecord()
            .RemoveAt(index)
            .ToCommand()
            .DoAndRecord(CommandRecorder.Default);
    }

    public void MoveItem(int oldIndex, int newIndex)
    {
        List.Value!.BeginRecord()
            .Move(oldIndex, newIndex)
            .ToCommand()
            .DoAndRecord(CommandRecorder.Default);
    }

    public override void ReadFromJson(JsonObject json)
    {
        base.ReadFromJson(json);
        try
        {
            IsExpanded.Value = (bool)json[nameof(IsExpanded)]!;

            if (json.TryGetPropertyValue(nameof(Items), out JsonNode? propsNode)
                && propsNode is JsonArray propsArray)
            {
                foreach ((JsonNode? node, ListItemEditorViewModel<TItem>? context) in propsArray.Zip(Items))
                {
                    if (context != null && node != null)
                    {
                        context.ReadFromJson(node.AsObject());
                    }
                }
            }
        }
        catch
        {
        }
    }

    public override void WriteToJson(JsonObject json)
    {
        base.WriteToJson(json);
        try
        {
            json[nameof(IsExpanded)] = IsExpanded.Value;

            var array = new JsonArray();

            foreach (ListItemEditorViewModel<TItem> item in Items.GetMarshal().Value)
            {
                if (item == null)
                {
                    array.Add(null);
                }
                else
                {
                    var node = new JsonObject();
                    item.WriteToJson(node);
                    array.Add(node);
                }
            }

            json[nameof(Items)] = array;
        }
        catch
        {
        }
    }

    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        AcceptChild();
    }

    private void AcceptChild()
    {
        var visitor = new Visitor(this);
        foreach (ListItemEditorViewModel<TItem> item in Items)
        {
            item.Context?.Accept(visitor);
        }
    }

    private sealed record Visitor(ListEditorViewModel<TItem> Obj) : IServiceProvider, IPropertyEditorContextVisitor
    {
        public object? GetService(Type serviceType)
        {
            return Obj.GetService(serviceType);
        }

        public void Visit(IPropertyEditorContext context)
        {
        }
    }
}
