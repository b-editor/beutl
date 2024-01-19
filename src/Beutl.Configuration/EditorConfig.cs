using System.ComponentModel;

using Beutl.Collections;
using Beutl.Serialization;

namespace Beutl.Configuration;

public enum LibraryTabDisplayMode
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
        LibraryTabDisplayModes.CollectionChanged += (_, _) => OnChanged();
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

    public CoreDictionary<string, LibraryTabDisplayMode> LibraryTabDisplayModes { get; } = new()
    {
        ["Search"] = LibraryTabDisplayMode.Show,
        ["Easings"] = LibraryTabDisplayMode.Show,
        ["Library"] = LibraryTabDisplayMode.Show,
        ["Nodes"] = LibraryTabDisplayMode.Hide,
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

        context.SetValue(nameof(LibraryTabDisplayModes), LibraryTabDisplayModes);
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);
        Dictionary<string, LibraryTabDisplayMode>? items
            = context.GetValue<Dictionary<string, LibraryTabDisplayMode>>(nameof(LibraryTabDisplayModes));

        if (items != null)
        {
            LibraryTabDisplayModes.Clear();
            foreach (KeyValuePair<string, LibraryTabDisplayMode> item in items)
            {
                LibraryTabDisplayModes.TryAdd(item.Key, item.Value);
            }
        }
    }
}
