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

    public GroupLibraryItem(string displayName, string? description, string? id)
        : this(displayName, description)
    {
        Id = id;
    }

    /// <summary>The language-independent ID of this group, if one was registered.</summary>
    public string? Id { get; private set; }

    // A copy, since a registration or an unload on another thread can change the items while they are read.
    public IReadOnlyList<LibraryItem> Items
    {
        get
        {
            lock (_lock)
            {
                return [.. _items];
            }
        }
    }

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
            MergeInto(_items, item);
        }
    }

    internal static void MergeInto(List<LibraryItem> items, LibraryItem item)
    {
        if (item is GroupLibraryItem group)
        {
            IEnumerable<GroupLibraryItem> groups = items.OfType<GroupLibraryItem>();
            GroupLibraryItem? existing = group.Id == null ? null : groups.FirstOrDefault(other => other.Id == group.Id);
            existing ??= groups.FirstOrDefault(other => (group.Id == null || other.Id == null)
                && other.DisplayName == group.DisplayName);

            if (existing != null)
            {
                existing.Id ??= group.Id;
                existing.Merge(group.Items);
                return;
            }
        }

        items.Add(item);
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
        return AddMultiple(displayName, null, action);
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
        return AddGroup(displayName, description, null, action);
    }

    /// <summary>Adds a subgroup with a language-independent ID. Groups without IDs can still merge by display name.</summary>
    public GroupLibraryItem AddGroup(string displayName, string? description, string? id, Action<GroupLibraryItem> action)
    {
        var item = new GroupLibraryItem(displayName, description, id);
        action(item);
        Merge(item);

        return this;
    }

    public GroupLibraryItem AddGroup(string displayName, Action<GroupLibraryItem> action)
    {
        return AddGroup(displayName, null, action);
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

    internal bool RemoveUnloaded(Type[] types)
    {
        lock (_lock)
        {
            return LibraryService.RemoveUnloaded(_items, types);
        }
    }
}

public static class KnownLibraryItemFormats
{
    public const string EngineObject = "Beutl.Engine.EngineObject";
    public const string GraphNode = "Beutl.NodeGraph.GraphNode";
    public const string Drawable = "Beutl.Graphics.Drawable";
    public const string Sound = "Beutl.Audio.Sound";
    public const string Transform = "Beutl.Graphics.Transformation.Transform";
    public const string FilterEffect = "Beutl.Graphics.Effects.FilterEffect";
    public const string AudioEffect = "Beutl.Audio.Effects.AudioEffect";
    public const string Brush = "Beutl.Media.Brush";
    public const string Easing = "Beutl.Animation.Easings.Easing";
    public const string Geometry = "Beutl.Media.Geometry";
    public const string Pen = "Beutl.Media.Pen";
    public const string ClipTransition = "Beutl.Graphics.Transitions.ClipTransition";
}

/// <summary>Language-independent IDs for the built-in library groups and categories.</summary>
public static class KnownLibraryGroupIds
{
    public const string ThreeD = "Beutl.Library.ThreeD";
    public const string AudioVisualizer = "Beutl.Library.AudioVisualizer";
    public const string Transform = "Beutl.Library.Transform";
    public const string ClipTransition = "Beutl.Library.ClipTransition";

    public const string FilterEffect = "Beutl.Library.FilterEffect";
    public const string FilterEffectBlurAndSharpen = FilterEffect + ".BlurAndSharpen";
    public const string FilterEffectColorCorrection = FilterEffect + ".ColorCorrection";
    public const string FilterEffectLightAndShadow = FilterEffect + ".LightAndShadow";
    public const string FilterEffectMaskingAndKeying = FilterEffect + ".MaskingAndKeying";
    public const string FilterEffectTransformAndDistort = FilterEffect + ".TransformAndDistort";
    public const string FilterEffectTimeAndAnimation = FilterEffect + ".TimeAndAnimation";
    public const string FilterEffectStylize = FilterEffect + ".Stylize";
    public const string FilterEffectCompositing = FilterEffect + ".Compositing";
    public const string FilterEffectScriptAndNodes = FilterEffect + ".ScriptAndNodes";

    public const string AudioEffect = "Beutl.Library.AudioEffect";
    public const string AudioEffectEqAndCleanup = AudioEffect + ".EqAndCleanup";
    public const string AudioEffectDynamics = AudioEffect + ".Dynamics";
    public const string AudioEffectTimeAndPitch = AudioEffect + ".TimeAndPitch";
    public const string AudioEffectDelayAndModulation = AudioEffect + ".DelayAndModulation";
}

public sealed class LibraryService : IDisposable
{
    private readonly List<LibraryItem> _items = [];
    private readonly Dictionary<string, HashSet<Type>> _formatToType = [];
    private readonly object _lock = new();

    public LibraryService()
    {
        TypeUnloadNotifier.TypesUnloading += Unregister;
    }

    public void Dispose()
    {
        TypeUnloadNotifier.TypesUnloading -= Unregister;
    }

    // A copy, since a registration or an unload on another thread can change the items while they are read.
    public IReadOnlyList<LibraryItem> Items
    {
        get
        {
            lock (_lock)
            {
                return [.. _items];
            }
        }
    }

    /// <summary>
    /// Raised after items are registered or unregistered, on the thread that changed them.
    /// </summary>
    public event EventHandler? ItemsChanged;

    public IReadOnlySet<Type> GetTypesFromFormat(string format)
    {
        lock (_lock)
        {
            return _formatToType.TryGetValue(format, out HashSet<Type>? types) ? types.ToHashSet() : [];
        }
    }

    private void Register(LibraryItem item)
    {
        lock (_lock)
        {
            item.SetLibraryService(this);
            GroupLibraryItem.MergeInto(_items, item);
        }

        ItemsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Register<T>(string format, string displayName, string? description = null)
    {
        Register(new SingleTypeLibraryItem(format, typeof(T), displayName, description));
    }

    public void AddMultiple(string displayName, Action<MultipleTypeLibraryItem> action)
    {
        AddMultiple(displayName, null, action);
    }

    public void AddMultiple(string displayName, string? description, Action<MultipleTypeLibraryItem> action)
    {
        var item = new MultipleTypeLibraryItem(displayName, description);
        action(item);
        Register(item);
    }

    public void RegisterGroup(string displayName, string? description, Action<GroupLibraryItem> action)
    {
        RegisterGroup(displayName, description, null, action);
    }

    /// <summary>Registers a group with a language-independent ID. Groups without IDs can still merge by display name.</summary>
    public void RegisterGroup(string displayName, string? description, string? id, Action<GroupLibraryItem> action)
    {
        var item = new GroupLibraryItem(displayName, description, id);
        action(item);
        Register(item);
    }

    public void RegisterGroup(string displayName, Action<GroupLibraryItem> action)
    {
        RegisterGroup(displayName, null, action);
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

    // Removes the items whose types unload, also from groups, and the groups that removing them leaves empty.
    internal static bool RemoveUnloaded(List<LibraryItem> items, Type[] types)
    {
        bool removed = false;
        for (int i = items.Count - 1; i >= 0; i--)
        {
            bool remove;
            if (items[i] is GroupLibraryItem group)
            {
                bool changed = group.RemoveUnloaded(types);
                removed |= changed;
                remove = changed && group.Items.Count == 0;
            }
            else
            {
                remove = items[i] switch
                {
                    SingleTypeLibraryItem single => types.Contains(single.ImplementationType),
                    MultipleTypeLibraryItem multi => multi.Types.Values.Any(types.Contains),
                    _ => false
                };
            }

            if (remove)
            {
                items.RemoveAt(i);
                removed = true;
            }
        }

        return removed;
    }

    private void Unregister(Type[] types)
    {
        bool removed;
        lock (_lock)
        {
            removed = RemoveUnloaded(_items, types);

            foreach (HashSet<Type> hashSet in _formatToType.Values)
            {
                removed |= hashSet.RemoveWhere(t => types.Contains(t)) > 0;
            }
        }

        if (removed)
        {
            ItemsChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
