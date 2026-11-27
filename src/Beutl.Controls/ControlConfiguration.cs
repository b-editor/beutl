using Avalonia;
using Beutl.Configuration;

namespace Beutl.Controls;

public sealed partial class ControlConfiguration
{
    [GeneratedAttachedProperty(Inherits = true)]
    public static partial GlobalConfiguration? GetConfiguration(AvaloniaObject element);
}
