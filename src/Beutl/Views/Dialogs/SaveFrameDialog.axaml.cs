using FluentAvalonia.UI.Controls;

namespace Beutl.Views.Dialogs;

public partial class SaveFrameDialog : FAContentDialog
{
    public SaveFrameDialog()
    {
        InitializeComponent();
    }

    protected override Type StyleKeyOverride => typeof(FAContentDialog);
}
