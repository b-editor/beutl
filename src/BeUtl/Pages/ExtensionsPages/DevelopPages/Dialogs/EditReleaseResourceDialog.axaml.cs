using Avalonia.Styling;

using Beutl.Api;

using BeUtl.ViewModels.ExtensionsPages.DevelopPages.Dialogs;

using FluentAvalonia.UI.Controls;

namespace BeUtl.Pages.ExtensionsPages.DevelopPages.Dialogs;

public sealed partial class EditReleaseResourceDialog : ContentDialog, IStyleable
{
    public EditReleaseResourceDialog()
    {
        InitializeComponent();
    }

    Type IStyleable.StyleKey => typeof(ContentDialog);

    protected override async void OnPrimaryButtonClick(ContentDialogButtonClickEventArgs args)
    {
        base.OnPrimaryButtonClick(args);
        if (DataContext is EditReleaseResourceDialogViewModel viewModel)
        {
            ContentDialogButtonClickDeferral deferral = args.GetDeferral();

            if (await viewModel.ApplyAsync())
            {
                deferral.Complete();
            }
        }
    }
}
