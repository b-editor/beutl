using Avalonia.Styling;

using Beutl.Api.Objects;

using BeUtl.ViewModels.ExtensionsPages.DevelopPages.Dialogs;

using FluentAvalonia.UI.Controls;

namespace BeUtl.Pages.ExtensionsPages.DevelopPages.Dialogs;

public sealed partial class AddReleaseResourceDialog : ContentDialog, IStyleable
{
    public AddReleaseResourceDialog()
    {
        InitializeComponent();
    }

    Type IStyleable.StyleKey => typeof(ContentDialog);

    protected override async void OnPrimaryButtonClick(ContentDialogButtonClickEventArgs args)
    {
        base.OnPrimaryButtonClick(args);
        if (DataContext is AddReleaseResourceDialogViewModel viewModel)
        {
            ContentDialogButtonClickDeferral deferral = args.GetDeferral();

            ReleaseResource? result = await viewModel.AddAsync();
            if (result != null)
            {
                deferral.Complete();
            }
        }
    }
}
