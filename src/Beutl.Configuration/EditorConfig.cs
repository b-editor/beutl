using System.ComponentModel;

namespace Beutl.Configuration;

public sealed class EditorConfig : ConfigurationBase
{
    public static readonly CoreProperty<bool> AutoAdjustSceneDurationProperty;
    public static readonly CoreProperty<bool> AdjustOutOfScreenCursorProperty;

    static EditorConfig()
    {
        AutoAdjustSceneDurationProperty = ConfigureProperty<bool, EditorConfig>(nameof(AutoAdjustSceneDuration))
            .DefaultValue(true)
            .Register();

        AdjustOutOfScreenCursorProperty = ConfigureProperty<bool, EditorConfig>(nameof(AdjustOutOfScreenCursor))
            .DefaultValue(true)
            .Register();
    }

    public bool AutoAdjustSceneDuration
    {
        get => GetValue(AutoAdjustSceneDurationProperty);
        set => SetValue(AutoAdjustSceneDurationProperty, value);
    }
    
    public bool AdjustOutOfScreenCursor
    {
        get => GetValue(AdjustOutOfScreenCursorProperty);
        set => SetValue(AdjustOutOfScreenCursorProperty, value);
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
