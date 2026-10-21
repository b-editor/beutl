using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Beutl.Editor.Components.Helpers;

namespace Beutl.Editor.Components.AiWorkspaceTab.Views;

// The offer of the Pro plan an AI tab shows when the account cannot use AI.
public partial class AiJoinProPanel : UserControl
{
    public AiJoinProPanel()
    {
        InitializeComponent();
    }

    // The tab's usage tracking prefix, such as AiWorkspace.ImageEdit. The button records it with the name of its
    // command, the identifier the usage dashboards count.
    public string? Feature
    {
        get;
        set
        {
            field = value;
            UsageTracking.SetFeature(JoinButton, value is null ? null : $"{value}.OpenAiPlan");
        }
    }

    // Like a layout panel, this is not an element of its own to assistive technology; only its controls are.
    protected override AutomationPeer OnCreateAutomationPeer() => new NoneAutomationPeer(this);
}
