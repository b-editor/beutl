using Avalonia.Platform.Storage;

using Beutl.Api.Objects;

using Beutl.ViewModels.ExtensionsPages.DevelopPages.Dialogs;

using FluentAvalonia.UI.Controls;

namespace Beutl.Pages.ExtensionsPages.DevelopPages.Dialogs;

public sealed partial class CreatePackageDialog : ContentDialog
{
    public CreatePackageDialog()
    {
        InitializeComponent();
        fileInput.OpenOptions = new FilePickerOpenOptions
        {
            FileTypeFilter = new[]
            {
                SharedFilePickerOptions.NuGetPackageManifestFileType,
                SharedFilePickerOptions.NuGetPackageFileType,
            }
        };
    }

    protected override Type StyleKeyOverride => typeof(ContentDialog);

    protected override async void OnPrimaryButtonClick(ContentDialogButtonClickEventArgs args)
    {
        base.OnPrimaryButtonClick(args);
        if (DataContext is CreatePackageDialogViewModel viewModel)
        {
            args.Cancel = true;
            IsEnabled = false;
            Package? result = await viewModel.CreateAsync();
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
