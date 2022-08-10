using BeUtl.Media;

namespace BeUtl.Streaming;

public class OperatorRegistry
{
    private static readonly List<BaseRegistryItem> s_operations = new();

    public static void RegisterOperation<T>(IObservable<string> displayName)
        where T : StreamOperator, new()
    {
        Register(new RegistryItem(displayName, Colors.Teal, typeof(T)));
    }

    public static void RegisterOperation<T>(IObservable<string> displayName, Color accentColor)
        where T : StreamOperator, new()
    {
        Register(new RegistryItem(displayName, accentColor, typeof(T)));
    }

    public static void RegisterOperation<T>(
        IObservable<string> displayName,
        Color accentColor,
        Func<string, bool> canOpen,
        Func<string, StreamOperator> openFile)
        where T : StreamOperator, new()
    {
        ArgumentNullException.ThrowIfNull(canOpen);
        ArgumentNullException.ThrowIfNull(openFile);

        Register(new RegistryItem(displayName, accentColor, typeof(T))
        {
            CanOpen = canOpen,
            OpenFile = openFile
        });
    }

    public static RegistrationHelper RegisterOperations(IObservable<string> displayName)
    {
        return RegisterOperations(displayName, Colors.Teal);
    }

    public static RegistrationHelper RegisterOperations(IObservable<string> displayName, Color accentColor)
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
        if (item is GroupableRegistryItem groupable
            && s_operations.FirstOrDefault(x => ReferenceEquals(x.DisplayName, item.DisplayName)) is GroupableRegistryItem registered)
        {
            registered.Merge(groupable.Items);
        }
        else
        {
            s_operations.Add(item);
        }
    }

    public record BaseRegistryItem(IObservable<string> DisplayName, Color AccentColor);

    public record RegistryItem(IObservable<string> DisplayName, Color AccentColor, Type Type)
        : BaseRegistryItem(DisplayName, AccentColor)
    {
        public Func<string, StreamOperator>? OpenFile { get; init; }

        public Func<string, bool>? CanOpen { get; init; }
    }

    public record GroupableRegistryItem(IObservable<string> DisplayName, Color AccentColor)
        : BaseRegistryItem(DisplayName, AccentColor)
    {
        public List<BaseRegistryItem> Items { get; } = new();

        internal void Merge(List<BaseRegistryItem> items)
        {
            foreach (BaseRegistryItem item in items)
            {
                if (item is GroupableRegistryItem groupable1
                    && Items.FirstOrDefault(x => ReferenceEquals(x.DisplayName, item.DisplayName)) is GroupableRegistryItem groupable2)
                {
                    groupable2.Merge(groupable1.Items);
                }
                else
                {
                    Items.Add(item);
                }
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
            _register = register ?? (item => OperatorRegistry.Register(item));
        }

        public RegistrationHelper Add<T>(IObservable<string> displayName)
            where T : StreamOperator, new()
        {
            _item.Items!.Add(new RegistryItem(displayName, Colors.Teal, typeof(T)));

            return this;
        }

        public RegistrationHelper Add<T>(IObservable<string> displayName, Color accentColor)
            where T : StreamOperator, new()
        {
            _item.Items.Add(new RegistryItem(displayName, accentColor, typeof(T)));

            return this;
        }

        public RegistrationHelper Add<T>(
            IObservable<string> displayName,
            Color accentColor,
            Func<string, bool> canOpen,
            Func<string, StreamOperator> openFile)
            where T : StreamOperator, new()
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

        public RegistrationHelper AddGroup(IObservable<string> displayName, Action<RegistrationHelper> action)
        {
            var item = new GroupableRegistryItem(displayName, Colors.Teal);
            var helper = new RegistrationHelper(item, x => _item.Items.Add(x));

            action(helper);

            return this;
        }

        public RegistrationHelper AddGroup(IObservable<string> displayName, Action<RegistrationHelper> action, Color accentColor)
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
