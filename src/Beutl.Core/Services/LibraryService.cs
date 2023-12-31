namespace Beutl.Services;

public abstract class LibraryItem
{
    protected LibraryItem(string displayName, string? description = null)
    {
        DisplayName = displayName;
        Description = description;
    }

    public string DisplayName { get; }

    public string? Description { get; }

    protected internal LibraryService? LibraryService { get; private set; }

    protected internal virtual void SetLibraryService(LibraryService libraryService)
    {
        if (LibraryService == null)
        {
            libraryService.Accepts(this);
        }

        LibraryService = libraryService;
    }
}

public sealed class MultipleTypeLibraryItem(string displayName, string? description = null) : LibraryItem(displayName, description)
{
    private readonly Dictionary<string, Type> _types = [];

    public IReadOnlyDictionary<string, Type> Types => _types;

    public MultipleTypeLibraryItem Bind<T>(string format)
    {
        _types[format] = typeof(T);

        return this;
    }
}

public sealed class SingleTypeLibraryItem(
    string format,
    Type implementationType,
    string displayName,
    string? description = null)
    : LibraryItem(displayName, description)
{
    public string Format { get; } = format;

    public Type ImplementationType { get; } = implementationType;
}

public sealed class GroupLibraryItem(string displayName, string? description = null)
    : LibraryItem(displayName, description)
{
    private readonly List<LibraryItem> _items = [];
    private readonly object _lock = new();

    public IReadOnlyList<LibraryItem> Items => _items;

    public void Merge(IReadOnlyList<LibraryItem> items)
    {
        foreach (LibraryItem item in items)
        {
            Merge(item);
        }
    }

    public void Merge(LibraryItem item)
    {
        lock (_lock)
        {
            if (item is GroupLibraryItem group1
                && Items.FirstOrDefault(x => x.DisplayName == item.DisplayName) is GroupLibraryItem group2)
            {
                group2.Merge(group1.Items);
            }
            else
            {
                _items.Add(item);
            }
        }
    }

    public GroupLibraryItem Add<T>(string format, string displayName, string? description = null)
    {
        lock (_lock)
        {
            _items.Add(new SingleTypeLibraryItem(format, typeof(T), displayName, description));
        }

        return this;
    }

    public GroupLibraryItem AddMultiple(string displayName, Action<MultipleTypeLibraryItem> action)
    {
        var item = new MultipleTypeLibraryItem(displayName);
        action(item);
        lock (_lock)
        {
            _items.Add(item);
        }

        return this;
    }

    public GroupLibraryItem AddMultiple(string displayName, string? description, Action<MultipleTypeLibraryItem> action)
    {
        var item = new MultipleTypeLibraryItem(displayName, description);
        action(item);
        lock (_lock)
        {
            _items.Add(item);
        }

        return this;
    }

    public GroupLibraryItem AddGroup(string displayName, string? description, Action<GroupLibraryItem> action)
    {
        var item = new GroupLibraryItem(displayName, description);
        action(item);
        Merge(item);

        return this;
    }

    public GroupLibraryItem AddGroup(string displayName, Action<GroupLibraryItem> action)
    {
        var item = new GroupLibraryItem(displayName);
        action(item);
        Merge(item);

        return this;
    }

    protected internal override void SetLibraryService(LibraryService libraryService)
    {
        base.SetLibraryService(libraryService);
        lock (_lock)
        {
            foreach (LibraryItem item in _items)
            {
                item.SetLibraryService(libraryService);
            }
        }
    }

    internal int Count()
    {
        int count = 1;
        lock (_lock)
        {
            foreach (LibraryItem item in Items)
            {
                if (item is GroupLibraryItem groupable1)
                {
                    count += groupable1.Count();
                }
                else
                {
                    count++;
                }
            }
        }

        return count;
    }
}

public static class KnownLibraryItemFormats
{
    public const string SourceOperator = "Beutl.Operation.SourceOperator";
    public const string Node = "Beutl.NodeTree.Node";
    public const string Drawable = "Beutl.Graphics.Drawable";
    public const string Sound = "Beutl.Audio.Sound";
    public const string Transform = "Beutl.Graphics.Transformation.Transform";
    public const string FilterEffect = "Beutl.Graphics.Effects.FilterEffect";
    public const string SoundEffect = "Beutl.Audio.Effects.SoundEffect";
    public const string Brush = "Beutl.Media.Brush";
    public const string Easing = "Beutl.Animation.Easings.Easing";
    public const string Geometry = "Beutl.Media.Geometry";
}

public sealed class LibraryService
{
    private readonly List<LibraryItem> _items = [];
    private readonly Dictionary<string, HashSet<Type>> _formatToType = [];
    private readonly object _lock = new();
    internal int _totalCount;

    public static LibraryService Current { get; } = new();

    public IReadOnlyList<LibraryItem> Items => _items;

    public IReadOnlySet<Type> GetTypesFromFormat(string format)
    {
        return GetHashSet(format);
    }

    private void Register(LibraryItem item)
    {
        lock (_lock)
        {
            item.SetLibraryService(this);
            if (item is GroupLibraryItem group)
            {
                _totalCount += group.Count();

                if (_items.FirstOrDefault(x => x.DisplayName == item.DisplayName) is GroupLibraryItem registered)
                {
                    registered.Merge(group.Items);
                }
                else
                {
                    _items.Add(group);
                }
            }
            else
            {
                _totalCount++;
                _items.Add(item);
            }
        }
    }

    public void Register<T>(string format, string displayName, string? description = null)
    {
        Register(new SingleTypeLibraryItem(format, typeof(T), displayName, description));
    }

    public void AddMultiple(string displayName, Action<MultipleTypeLibraryItem> action)
    {
        var item = new MultipleTypeLibraryItem(displayName);
        action(item);
        Register(item);
    }

    public void AddMultiple(string displayName, string? description, Action<MultipleTypeLibraryItem> action)
    {
        var item = new MultipleTypeLibraryItem(displayName, description);
        action(item);
        Register(item);
    }

    public void RegisterGroup(string displayName, string? description, Action<GroupLibraryItem> action)
    {
        var item = new GroupLibraryItem(displayName, description);
        action(item);
        Register(item);
    }

    public void RegisterGroup(string displayName, Action<GroupLibraryItem> action)
    {
        var item = new GroupLibraryItem(displayName);
        action(item);
        Register(item);
    }

    private HashSet<Type> GetHashSet(string format)
    {
        if (_formatToType.TryGetValue(format, out HashSet<Type>? hashset))
        {
            return hashset;
        }
        else
        {
            hashset = [];
            _formatToType.Add(format, hashset);
            return hashset;
        }
    }

    internal void Accepts(LibraryItem item)
    {
        if (item is SingleTypeLibraryItem single)
        {
            HashSet<Type> hashset = GetHashSet(single.Format);
            hashset.Add(single.ImplementationType);
        }
        else if (item is MultipleTypeLibraryItem multiple)
        {
            foreach ((string format, Type type) in multiple.Types)
            {
                HashSet<Type> hashset = GetHashSet(format);
                hashset.Add(type);
            }
        }
    }

    public LibraryItem? FindItem(Type type)
    {
        static LibraryItem? Find(IReadOnlyList<LibraryItem> list, Type type)
        {
            for (int i = 0; i < list.Count; i++)
            {
                LibraryItem item = list[i];

                if (item is SingleTypeLibraryItem single && single.ImplementationType == type)
                {
                    return single;
                }
                else if (item is MultipleTypeLibraryItem multi && multi.Types.Values.Contains(type))
                {
                    return multi;
                }
                else if (item is GroupLibraryItem group)
                {
                    LibraryItem? result = Find(group.Items, type);
                    if (result != null)
                    {
                        return result;
                    }
                }
            }

            return null;
        }

        lock (_lock)
        {
            return Find(_items, type);
        }
    }
}
