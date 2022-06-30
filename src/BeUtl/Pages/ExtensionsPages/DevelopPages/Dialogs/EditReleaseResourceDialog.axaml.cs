using Avalonia.Styling;

using FluentAvalonia.UI.Controls;

namespace BeUtl.Pages.ExtensionsPages.DevelopPages.Dialogs;

public sealed partial class EditReleaseResourceDialog : ContentDialog, IStyleable
{
    public EditReleaseResourceDialog()
    {
        InitializeComponent();
    }

    Type IStyleable.StyleKey => typeof(ContentDialog);
}
