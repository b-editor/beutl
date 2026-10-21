using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Beutl.Editor.Components.Helpers;

namespace Beutl.Editor.Components.AiWorkspaceTab.Views;

// The requests an AI tab can recover or abandon, shown while there are any.
public partial class AiRecoveryPanel : UserControl
{
    public AiRecoveryPanel()
    {
        InitializeComponent();
    }

    // The tab's usage tracking prefix, such as AiWorkspace.ImageEdit. Each button records it with the name of its
    // command, the identifier the usage dashboards count.
    public string? Feature
    {
        get;
        set
        {
            field = value;
            UsageTracking.SetFeature(RecoverButton, value is null ? null : $"{value}.RecoverSelectedAttempt");
            UsageTracking.SetFeature(AbandonButton, value is null ? null : $"{value}.AbandonSelectedAttempt");
        }
    }

    // Like a layout panel, this is not an element of its own to assistive technology; only its controls are.
    protected override AutomationPeer OnCreateAutomationPeer() => new NoneAutomationPeer(this);
}
