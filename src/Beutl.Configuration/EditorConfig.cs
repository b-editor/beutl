using System.ComponentModel;

using Beutl.Collections;
using Beutl.Serialization;

namespace Beutl.Configuration;

public enum LibraryNavigationDisplayMode
{
    Show,
    Hide
}

public sealed class EditorConfig : ConfigurationBase
{
    public static readonly CoreProperty<bool> AutoAdjustSceneDurationProperty;
    public static readonly CoreProperty<bool> EnablePointerLockInPropertyProperty;

    static EditorConfig()
    {
        AutoAdjustSceneDurationProperty = ConfigureProperty<bool, EditorConfig>(nameof(AutoAdjustSceneDuration))
            .DefaultValue(true)
            .Register();

        EnablePointerLockInPropertyProperty = ConfigureProperty<bool, EditorConfig>(nameof(EnablePointerLockInProperty))
            .DefaultValue(true)
            .Register();
    }

    public EditorConfig()
    {
        LibraryNavigationDisplayModes.CollectionChanged += (_, _) => OnChanged();
    }

    public bool AutoAdjustSceneDuration
    {
        get => GetValue(AutoAdjustSceneDurationProperty);
        set => SetValue(AutoAdjustSceneDurationProperty, value);
    }

    public bool EnablePointerLockInProperty
    {
        get => GetValue(EnablePointerLockInPropertyProperty);
        set => SetValue(EnablePointerLockInPropertyProperty, value);
    }

    public CoreDictionary<string, LibraryNavigationDisplayMode> LibraryNavigationDisplayModes { get; } = new()
    {
        ["Search"] = LibraryNavigationDisplayMode.Show,
        ["Easings"] = LibraryNavigationDisplayMode.Show,
        ["Library"] = LibraryNavigationDisplayMode.Show,
        ["Nodes"] = LibraryNavigationDisplayMode.Show,
    };

    protected override void OnPropertyChanged(PropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);
        if (args.PropertyName is not (nameof(Id) or nameof(Name)))
        {
            OnChanged();
        }
    }

    public override void Serialize(ICoreSerializationContext context)
    {
        base.Serialize(context);

        context.SetValue(nameof(LibraryNavigationDisplayModes), LibraryNavigationDisplayModes);
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);
        Dictionary<string, LibraryNavigationDisplayMode>? items
            = context.GetValue<Dictionary<string, LibraryNavigationDisplayMode>>(nameof(LibraryNavigationDisplayModes));

        if (items != null)
        {
            LibraryNavigationDisplayModes.Clear();
            foreach (KeyValuePair<string, LibraryNavigationDisplayMode> item in items)
            {
                LibraryNavigationDisplayModes.TryAdd(item.Key, item.Value);
            }
        }
    }
}
