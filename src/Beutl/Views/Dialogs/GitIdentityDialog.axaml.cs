using FluentAvalonia.UI.Controls;

namespace Beutl.Views.Dialogs;

public sealed partial class GitIdentityDialog : ContentDialog
{
    public GitIdentityDialog()
    {
        InitializeComponent();
    }

    protected override Type StyleKeyOverride => typeof(ContentDialog);
}
