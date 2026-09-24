#nullable enable
using Avalonia.Automation.Peers;
using Avalonia.Controls;

namespace Beutl.Controls;

/// <summary>
/// Keeps live-region announcements enabled without passing an empty name to
/// Avalonia.Native on macOS, where that name becomes a nil NSDictionary value.
/// </summary>
public class SafeLiveRegionTextBlock : TextBlock
{
    protected override AutomationPeer OnCreateAutomationPeer()
        => new SafeLiveRegionTextBlockAutomationPeer(this);

    private sealed class SafeLiveRegionTextBlockAutomationPeer(TextBlock owner)
        : TextBlockAutomationPeer(owner)
    {
        protected override string? GetNameCore()
        {
            string? name = base.GetNameCore();
            return OperatingSystem.IsMacOS() && string.IsNullOrEmpty(name) ? "\u200B" : name;
        }
    }
}
