using System.Collections.Specialized;

using Beutl.Framework;
using Beutl.Services;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

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

public sealed class ListItemEditorViewModel<TItem> : IDisposable
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
}

public sealed class ListEditorViewModel<TItem> : BaseEditorViewModel
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

        ObserveCount = Items.ObserveProperty(o => o.Count)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        CountString = ObserveCount
            .Select(x => string.Format(Message.CountItems, x))
            .ToReadOnlyReactivePropertySlim(string.Empty)
            .DisposeWith(Disposables);

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

                OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, changedItems: list.ToArray()));
            }
        }).DisposeWith(Disposables);
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnCollectionChanged(e);
    }

    private void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        void Added(int index, TItem? obj)
        {
            var itemAccessor = new ListItemAccessorImpl<TItem?>(index, obj, List.Value!);
            var item = new ListItemEditorViewModel<TItem>(this, itemAccessor);
            Items.Insert(index, item);
        }

        void Removed(int index)
        {
            ListItemEditorViewModel<TItem> item = Items[index];
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
                for (int i = List.Value!.Count - 1; i >= 0; --i)
                {
                    Removed(index + i);
                }

                UpdateIndex(index);
                break;

            case NotifyCollectionChangedAction.Replace:
                for (int i = e.NewStartingIndex; i < e.NewItems!.Count; i++)
                {
                    Items[i].ItemAccessor.OnItemChanged(List.Value![i]);
                }
                break;

            case NotifyCollectionChangedAction.Move:
                Items.MoveRange(e.OldStartingIndex, e.NewItems!.Count, e.NewStartingIndex);

                UpdateIndex(e.NewStartingIndex);
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

    public ReadOnlyReactivePropertySlim<IList<TItem?>?> List { get; }

    public CoreList<ListItemEditorViewModel<TItem>> Items { get; } = new();

    public ReadOnlyReactivePropertySlim<int> ObserveCount { get; }

    public ReadOnlyReactivePropertySlim<string> CountString { get; }

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

    public void AddItem(TItem? item)
    {
        List.Value!.BeginRecord()
            .Add(item)
            .ToCommand()
            .DoAndRecord(CommandRecorder.Default);
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
}
