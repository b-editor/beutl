using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Beutl.Editor.Components.Helpers;

namespace Beutl.Views.Tools;

// What an AI tab offers for its result: adding it to the scene or saving it to a file.
public partial class AiResultActionsPanel : UserControl
{
    public AiResultActionsPanel()
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
            UsageTracking.SetFeature(AddToSceneButton, value is null ? null : $"{value}.AddToScene");
            UsageTracking.SetFeature(SaveToFileButton, value is null ? null : $"{value}.SaveToFile");
        }
    }

    // Like a layout panel, this is not an element of its own to assistive technology; only its controls are.
    protected override AutomationPeer OnCreateAutomationPeer() => new NoneAutomationPeer(this);
}
