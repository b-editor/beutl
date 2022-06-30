using BeUtl.Media;

namespace BeUtl.ProjectSystem;

public class LayerOperationRegistry
{
    private static readonly List<BaseRegistryItem> s_operations = new();

    public static void RegisterOperation<T>(ResourceReference<string> displayName)
        where T : LayerOperation, new()
    {
        Register(new RegistryItem(displayName, Colors.Teal, typeof(T)));
    }

    public static void RegisterOperation<T>(ResourceReference<string> displayName, Color accentColor)
        where T : LayerOperation, new()
    {
        Register(new RegistryItem(displayName, accentColor, typeof(T)));
    }

    public static void RegisterOperation<T>(
        ResourceReference<string> displayName,
        Color accentColor,
        Func<string, bool> canOpen,
        Func<string, LayerOperation> openFile)
        where T : LayerOperation, new()
    {
        ArgumentNullException.ThrowIfNull(canOpen);
        ArgumentNullException.ThrowIfNull(openFile);

        Register(new RegistryItem(displayName, accentColor, typeof(T))
        {
            CanOpen = canOpen,
            OpenFile = openFile
        });
    }

    public static RegistrationHelper RegisterOperations(ResourceReference<string> displayName)
    {
        return RegisterOperations(displayName, Colors.Teal);
    }
    
    public static RegistrationHelper RegisterOperations(ResourceReference<string> displayName, Color accentColor)
    {
        return new RegistrationHelper(new GroupableRegistryItem(displayName, accentColor));
    }

    public static IList<BaseRegistryItem> GetRegistered()
    {
        return s_operations;
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

        for (int i = 0; i < s_operations.Count; i++)
        {
            BaseRegistryItem item = s_operations[i];

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
        s_operations.Add(item);
    }

    public record BaseRegistryItem(ResourceReference<string> DisplayName, Color AccentColor);

    public record RegistryItem(ResourceReference<string> DisplayName, Color AccentColor, Type Type)
        : BaseRegistryItem(DisplayName, AccentColor)
    {
        public Func<string, LayerOperation>? OpenFile { get; init; }

        public Func<string, bool>? CanOpen { get; init; }
    }

    public record GroupableRegistryItem(ResourceReference<string> DisplayName, Color AccentColor)
        : BaseRegistryItem(DisplayName, AccentColor)
    {
        public List<BaseRegistryItem> Items { get; } = new();
    }

    public class RegistrationHelper
    {
        private readonly GroupableRegistryItem _item;
        private readonly Action<GroupableRegistryItem> _register;

        internal RegistrationHelper(GroupableRegistryItem item, Action<GroupableRegistryItem>? register = null)
        {
            _item = item;
            _register = register ?? (item => LayerOperationRegistry.Register(item));
        }

        public RegistrationHelper Add<T>(ResourceReference<string> displayName)
            where T : LayerOperation, new()
        {
            _item.Items!.Add(new RegistryItem(displayName, Colors.Teal, typeof(T)));

            return this;
        }

        public RegistrationHelper Add<T>(ResourceReference<string> displayName, Color accentColor)
            where T : LayerOperation, new()
        {
            _item.Items.Add(new RegistryItem(displayName, accentColor, typeof(T)));

            return this;
        }

        public RegistrationHelper Add<T>(
            ResourceReference<string> displayName,
            Color accentColor,
            Func<string, bool> canOpen,
            Func<string, LayerOperation> openFile)
            where T : LayerOperation, new()
        {
            ArgumentNullException.ThrowIfNull(canOpen);
            ArgumentNullException.ThrowIfNull(openFile);

            _item.Items.Add(new RegistryItem(displayName, accentColor, typeof(T))
            {
                CanOpen = canOpen,
                OpenFile = openFile
            });

            return this;
        }

        public RegistrationHelper AddGroup(ResourceReference<string> displayName, Action<RegistrationHelper> action)
        {
            var item = new GroupableRegistryItem(displayName, Colors.Teal);
            var helper = new RegistrationHelper(item, x => _item.Items.Add(x));

            action(helper);

            return this;
        }
        
        public RegistrationHelper AddGroup(ResourceReference<string> displayName, Action<RegistrationHelper> action, Color accentColor)
        {
            var item = new GroupableRegistryItem(displayName, accentColor);
            var helper = new RegistrationHelper(item, x => _item.Items.Add(x));

            action(helper);

            return this;
        }

        public void Register()
        {
            _register(_item);
        }
    }
}
