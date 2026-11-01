using Avalonia.Controls;
using Beutl.Editor.Components.PropertyEditors.Views;

namespace Beutl.Editor.Components.ChannelMapProperties.Views;

public sealed partial class ChannelMapEditor : UserControl
{
    public ChannelMapEditor()
    {
        InitializeComponent();
        ExpandTransitionHelper.Attach(expandToggle, content);
    }
}
