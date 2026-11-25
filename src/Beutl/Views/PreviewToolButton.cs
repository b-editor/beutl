using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Automation.Peers;

namespace Beutl.Views;

internal sealed class PreviewToolButton : RadioButton
{
    protected override Type StyleKeyOverride => typeof(RadioButton);

    protected override void Toggle()
    {
        // The selected preview tool owns the checked state, including during a temporary pan.
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new PreviewToolButtonPeer(this);

    private sealed class PreviewToolButtonPeer(PreviewToolButton owner) : RadioButtonAutomationPeer(owner), ISelectionItemProvider
    {
        void ISelectionItemProvider.Select()
        {
            EnsureEnabled();
            ((PreviewToolButton)Owner).OnClick();
        }
    }
}
