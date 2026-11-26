using System.Diagnostics.CodeAnalysis;
using Beutl.Media;

namespace Beutl.NodeGraph;

public class GraphNodeRegistry
{
    private static readonly List<BaseRegistryItem> s_nodes = [];
    // Extensions register and unload nodes on other threads than the UI that reads them.
    private static readonly object s_lock = new();

    static GraphNodeRegistry()
    {
        TypeUnloadNotifier.TypesUnloading += Unregister;
    }

    /// <summary>
    /// Raised after nodes are registered or unregistered, on the thread that changed them.
    /// </summary>
    public static event EventHandler? Changed;

    public static void RegisterNode<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T>(
        string displayName)
        where T : GraphNode, new()
    {
        RegisterNode<T>(displayName, Colors.Teal);
    }

    public static void RegisterNode<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T>(
        string displayName, Color accentColor)
        where T : GraphNode, new()
    {
        Register(new RegistryItem(displayName, accentColor, typeof(T)));
    }

    public static RegistrationHelper RegisterNodes(string displayName)
    {
        return RegisterNodes(displayName, Colors.Teal);
    }

    public static RegistrationHelper RegisterNodes(string displayName, Color accentColor)
    {
        return new RegistrationHelper(new GroupableRegistryItem(displayName, accentColor));
    }

    public static IList<BaseRegistryItem> GetRegistered()
    {
        lock (s_lock)
        {
            return s_nodes.ConvertAll(Copy);
        }
    }

    public static RegistryItem? FindItem(Type type)
    {
        static RegistryItem? Find(List<BaseRegistryItem> list, Type type)
        {
            for (int i = 0; i < list.Count; i++)
            {
                BaseRegistryItem item = list[i];

                if (item is RegistryItem registryItem && registryItem.Type == type)
                {
                    return registryItem;
                }
                else if (item is GroupableRegistryItem groupable)
                {
                    RegistryItem? result = Find(groupable.Items, type);
                    if (result != null)
                    {
                        return result;
                    }
                }
            }

            return null;
        }

        lock (s_lock)
        {
            return Find(s_nodes, type);
        }
    }

    private static void Unregister(Type[] types)
    {
        bool changed;
        lock (s_lock)
        {
            changed = RemoveUnloaded(s_nodes, types);
        }

        if (changed)
            Changed?.Invoke(null, EventArgs.Empty);
    }

    private static bool RemoveUnloaded(List<BaseRegistryItem> items, Type[] types)
    {
        bool changed = false;
        for (int i = items.Count - 1; i >= 0; i--)
        {
            BaseRegistryItem item = items[i];
            if (item is GroupableRegistryItem group)
            {
                changed |= RemoveUnloaded(group.Items, types);
                if (group.Items.Count == 0)
                {
                    items.RemoveAt(i);
                    changed = true;
                }
            }
            else if (item is RegistryItem registryItem && types.Any(t => TypeUnloadNotifier.ContainsTypeRecursive(registryItem.Type, t)))
            {
                items.RemoveAt(i);
                changed = true;
            }
        }

        return changed;
    }

    private static void Register(BaseRegistryItem item)
    {
        // The caller keeps its group, so the registry stores a copy.
        item = Copy(item);
        lock (s_lock)
        {
            MergeInto(s_nodes, item);
        }

        Changed?.Invoke(null, EventArgs.Empty);
    }

    private static void MergeInto(List<BaseRegistryItem> items, BaseRegistryItem item)
    {
        if (item is GroupableRegistryItem group)
        {
            IEnumerable<GroupableRegistryItem> groups = items.OfType<GroupableRegistryItem>();
            GroupableRegistryItem? existing = group.Id == null ? null : groups.FirstOrDefault(other => other.Id == group.Id);
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

    private static BaseRegistryItem Copy(BaseRegistryItem item)
    {
        if (item is not GroupableRegistryItem group)
            return item;

        var copy = new GroupableRegistryItem(group.DisplayName, group.AccentColor) { Id = group.Id };
        copy.Items.AddRange(group.Items.Select(Copy));
        return copy;
    }

    public record BaseRegistryItem(string DisplayName, Color AccentColor);

    public record RegistryItem(
        string DisplayName,
        Color AccentColor,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
        Type Type)
        : BaseRegistryItem(DisplayName, AccentColor);

    public record GroupableRegistryItem(string DisplayName, Color AccentColor)
        : BaseRegistryItem(DisplayName, AccentColor)
    {
        internal string? Id { get; set; }

        public List<BaseRegistryItem> Items { get; } = [];

        internal void Merge(List<BaseRegistryItem> items)
        {
            foreach (BaseRegistryItem item in items)
            {
                MergeInto(Items, item);
            }
        }
    }

    public class RegistrationHelper
    {
        private readonly GroupableRegistryItem _item;
        private readonly Action<GroupableRegistryItem> _register;

        internal RegistrationHelper(GroupableRegistryItem item, Action<GroupableRegistryItem>? register = null)
        {
            _item = item;
            _register = register ?? (item => GraphNodeRegistry.Register(item));
        }

        internal RegistrationHelper WithId(string id)
        {
            _item.Id = id;
            return this;
        }

        public RegistrationHelper Add<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T>(
            string displayName)
            where T : GraphNode, new()
        {
            return Add<T>(displayName, _item.AccentColor);
        }

        public RegistrationHelper Add<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T>(
            string displayName, Color accentColor)
            where T : GraphNode, new()
        {
            _item.Items.Add(new RegistryItem(displayName, accentColor, typeof(T)));

            return this;
        }

        public RegistrationHelper AddGroup(string displayName, Action<RegistrationHelper> action)
        {
            return AddGroup(displayName, action, _item.AccentColor);
        }

        public RegistrationHelper AddGroup(string displayName, Action<RegistrationHelper> action, Color accentColor)
        {
            var item = new GroupableRegistryItem(displayName, accentColor);
            var helper = new RegistrationHelper(item, _item.Items.Add);

            action(helper);

            return this;
        }

        public void Register()
        {
            _register(_item);
        }
    }
}
