using System.Diagnostics.CodeAnalysis;
using System.Reactive.Linq;

using Beutl.Media;

namespace Beutl.NodeTree;

public class NodeRegistry
{
    private static readonly List<BaseRegistryItem> s_nodes = [];
    internal static int s_totalCount;

    public static void RegisterNode<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T>(
        string displayName)
        where T : Node, new()
    {
        Register(new RegistryItem(displayName, Colors.Teal, typeof(T)));
    }

    public static void RegisterNode<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T>(
        string displayName, Color accentColor)
        where T : Node, new()
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
        return s_nodes;
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

        RegistryItem? result = null;

        for (int i = 0; i < s_nodes.Count; i++)
        {
            BaseRegistryItem item = s_nodes[i];

            if (item is GroupableRegistryItem group)
            {
                result = Find(group.Items, type);
            }
            else if (item is RegistryItem registryItem && registryItem.Type == type)
            {
                result = registryItem;
            }

            if (result != null)
            {
                return result;
            }
        }

        return null;
    }

    private static void Register(BaseRegistryItem item)
    {
        if (item is GroupableRegistryItem groupable)
        {
            s_totalCount += groupable.Count();

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
            s_totalCount++;
            s_nodes.Add(item);
        }
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

        internal int Count()
        {
            int count = 1;
            foreach (BaseRegistryItem item in Items)
            {
                if (item is GroupableRegistryItem groupable1)
                {
                    count += groupable1.Count();
                }
                else
                {
                    count++;
                }
            }

            return count;
        }
    }

    public class RegistrationHelper
    {
        private readonly GroupableRegistryItem _item;
        private readonly Action<GroupableRegistryItem> _register;

        internal RegistrationHelper(GroupableRegistryItem item, Action<GroupableRegistryItem>? register = null)
        {
            _item = item;
            _register = register ?? (item => NodeRegistry.Register(item));
        }

        public RegistrationHelper Add<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T>(
            string displayName)
            where T : Node, new()
        {
            _item.Items!.Add(new RegistryItem(displayName, Colors.Teal, typeof(T)));

            return this;
        }

        public RegistrationHelper Add<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T>(
            string displayName, Color accentColor)
            where T : Node, new()
        {
            _item.Items.Add(new RegistryItem(displayName, accentColor, typeof(T)));

            return this;
        }

        public RegistrationHelper AddGroup(string displayName, Action<RegistrationHelper> action)
        {
            var item = new GroupableRegistryItem(displayName, Colors.Teal);
            var helper = new RegistrationHelper(item, _item.Items.Add);

            action(helper);

            return this;
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
