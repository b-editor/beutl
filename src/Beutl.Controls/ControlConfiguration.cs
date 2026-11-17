using Avalonia;
using Beutl.Configuration;

namespace Beutl.Controls;

public sealed class ControlConfiguration
{
    public static readonly AttachedProperty<GlobalConfiguration?> ConfigurationProperty =
        AvaloniaProperty.RegisterAttached<ControlConfiguration, StyledElement, GlobalConfiguration?>(
            "Configuration", inherits: true);

    public static GlobalConfiguration? GetConfiguration(AvaloniaObject element) => element.GetValue(ConfigurationProperty);

    public static void SetConfiguration(AvaloniaObject element, GlobalConfiguration? configuration) => element.SetValue(ConfigurationProperty, configuration);
}
