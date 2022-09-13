using Avalonia.Styling;

using Beutl.Api.Objects;

using BeUtl.ViewModels.ExtensionsPages.DevelopPages.Dialogs;

using FluentAvalonia.UI.Controls;

namespace BeUtl.Pages.ExtensionsPages.DevelopPages.Dialogs;

public sealed partial class CreatePackageDialog : ContentDialog, IStyleable
{
    public CreatePackageDialog()
    {
        InitializeComponent();
    }

    Type IStyleable.StyleKey => typeof(ContentDialog);

    protected override async void OnPrimaryButtonClick(ContentDialogButtonClickEventArgs args)
    {
        base.OnPrimaryButtonClick(args);
        if (DataContext is CreatePackageDialogViewModel viewModel)
        {
            ContentDialogButtonClickDeferral deferral = args.GetDeferral();

            Package? result = await viewModel.CreateAsync();
            if (result != null)
            {
                deferral.Complete();
            }
        }
    }
}
