using BEditorNext.Media;

namespace BEditorNext.ProjectSystem;

public class RenderOperationRegistry
{
    private static readonly List<BaseRegistryItem> s_operations = new();

    public static void RegisterOperation<T>(ResourceReference<string> displayName)
        where T : RenderOperation, new()
    {
        Register(new RegistryItem(displayName, Colors.Teal, typeof(T)));
    }

    public static void RegisterOperation<T>(ResourceReference<string> displayName, Color accentColor)
        where T : RenderOperation, new()
    {
        Register(new RegistryItem(displayName, accentColor, typeof(T)));
    }

    public static void RegisterOperation<T>(
        ResourceReference<string> displayName,
        Color accentColor,
        Func<string, bool> canOpen,
        Func<string, RenderOperation> openFile)
        where T : RenderOperation, new()
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

    private static void Register(BaseRegistryItem item)
    {
        s_operations.Add(item);
    }

    public record BaseRegistryItem(ResourceReference<string> DisplayName, Color AccentColor);

    public record RegistryItem(ResourceReference<string> DisplayName, Color AccentColor, Type Type)
        : BaseRegistryItem(DisplayName, AccentColor)
    {
        public Func<string, RenderOperation>? OpenFile { get; init; }

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
            where T : RenderOperation, new()
        {
            _item.Items!.Add(new RegistryItem(displayName, Colors.Teal, typeof(T)));

            return this;
        }

        public RegistrationHelper Add<T>(ResourceReference<string> displayName, Color accentColor)
            where T : RenderOperation, new()
        {
            _item.Items!.Add(new RegistryItem(displayName, accentColor, typeof(T)));

            return this;
        }

        public RegistrationHelper Add<T>(
            ResourceReference<string> displayName,
            Color accentColor,
            Func<string, bool> canOpen,
            Func<string, RenderOperation> openFile)
            where T : RenderOperation, new()
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
