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
        lock (s_lock)
        {
            for (int i = s_nodes.Count - 1; i >= 0; i--)
            {
                var item = s_nodes[i];
                if (item is GroupableRegistryItem groupable)
                {
                    if (groupable.Unregister(types))
                    {
                        s_nodes.RemoveAt(i);
                    }
                }
                else if (item is RegistryItem registryItem && types.Any(t => TypeUnloadNotifier.ContainsTypeRecursive(registryItem.Type, t)))
                {
                    s_nodes.RemoveAt(i);
                }
            }
        }
    }

    private static void Register(BaseRegistryItem item)
    {
        // The caller keeps its group, so the registry stores a copy.
        item = Copy(item);
        lock (s_lock)
        {
            if (item is GroupableRegistryItem groupable)
            {
                if (s_nodes.FirstOrDefault(x => x.DisplayName == item.DisplayName) is GroupableRegistryItem registered)
                {
                    registered.Merge(groupable.Items);
                }
                else
                {
                    s_nodes.Add(groupable);
                }
            }
            else
            {
                s_nodes.Add(item);
            }
        }
    }

    private static BaseRegistryItem Copy(BaseRegistryItem item)
    {
        if (item is not GroupableRegistryItem group)
            return item;

        var copy = new GroupableRegistryItem(group.DisplayName, group.AccentColor);
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
        public List<BaseRegistryItem> Items { get; } = [];

        internal void Merge(List<BaseRegistryItem> items)
        {
            foreach (BaseRegistryItem item in items)
            {
                if (item is GroupableRegistryItem groupable1
                    && Items.FirstOrDefault(x => x.DisplayName == item.DisplayName) is GroupableRegistryItem groupable2)
                {
                    groupable2.Merge(groupable1.Items);
                }
                else
                {
                    Items.Add(item);
                }
            }
        }

        internal bool Unregister(Type[] types)
        {
            for (int i = Items.Count - 1; i >= 0; i--)
            {
                BaseRegistryItem item = Items[i];

                if (item is GroupableRegistryItem groupable)
                {
                    if (groupable.Unregister(types))
                    {
                        Items.RemoveAt(i);
                    }
                }
                else if (item is RegistryItem registryItem && types.Any(t => TypeUnloadNotifier.ContainsTypeRecursive(registryItem.Type, t)))
                {
                    Items.RemoveAt(i);
                }
            }

            return Items.Count == 0;
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
