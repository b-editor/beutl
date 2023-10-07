using System.ComponentModel;

namespace Beutl.Configuration;

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

    protected override void OnPropertyChanged(PropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);
        if (args.PropertyName is not (nameof(Id) or nameof(Name)))
        {
            OnChanged();
        }
    }
}
