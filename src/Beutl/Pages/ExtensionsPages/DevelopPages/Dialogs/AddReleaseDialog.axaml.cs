using Beutl.Api.Objects;

using Beutl.ViewModels.ExtensionsPages.DevelopPages.Dialogs;

using FluentAvalonia.UI.Controls;

namespace Beutl.Pages.ExtensionsPages.DevelopPages.Dialogs;

public sealed partial class AddReleaseDialog : ContentDialog
{
    public AddReleaseDialog()
    {
        InitializeComponent();
    }

    protected override Type StyleKeyOverride => typeof(ContentDialog);

    protected override async void OnPrimaryButtonClick(ContentDialogButtonClickEventArgs args)
    {
        base.OnPrimaryButtonClick(args);
        if (DataContext is AddReleaseDialogViewModel viewModel)
        {
            args.Cancel = true;
            IsEnabled = false;
            Release? result = await viewModel.AddAsync();
            if (result != null)
            {
                Hide(ContentDialogResult.Primary);
            }
            else
            {
                IsEnabled = true;
            }
        }
    }
}
