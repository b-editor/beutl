using FluentAvalonia.UI.Controls;

namespace Beutl.Views.Dialogs;

public partial class SaveFrameDialog : ContentDialog
{
    public SaveFrameDialog()
    {
        InitializeComponent();
    }

    protected override Type StyleKeyOverride => typeof(ContentDialog);
}
