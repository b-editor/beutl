using Avalonia.Controls;

namespace Beutl.Editor.Components.WebBrowserTab;

internal interface IBrowserSettingsHost
{
    Task OpenBrowserSettingsAsync(Window owner);
}
