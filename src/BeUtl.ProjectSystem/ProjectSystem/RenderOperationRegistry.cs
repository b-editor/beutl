using BeUtl.Media;

namespace BeUtl.ProjectSystem;

public class RenderOperationRegistry
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
        static RegistryItem? Find(List<RegistryItem> list, Type type)
        {
            for (int i = 0; i < list.Count; i++)
            {
                RegistryItem item = list[i];

                if (item.Type == type)
                {
                    return item;
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
        public List<RegistryItem> Items { get; } = new();
    }

    public class RegistrationHelper
    {
        private readonly GroupableRegistryItem _item;

        internal RegistrationHelper(GroupableRegistryItem item)
        {
            _item = item;
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
            _item.Items!.Add(new RegistryItem(displayName, accentColor, typeof(T)));

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

            _item.Items!.Add(new RegistryItem(displayName, accentColor, typeof(T))
            {
                CanOpen = canOpen,
                OpenFile = openFile
            });

            return this;
        }

        public void Register()
        {
            RenderOperationRegistry.Register(_item);
        }
    }
}
