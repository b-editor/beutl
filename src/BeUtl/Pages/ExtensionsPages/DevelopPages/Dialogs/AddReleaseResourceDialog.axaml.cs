using Avalonia.Styling;

using FluentAvalonia.UI.Controls;

namespace BeUtl.Pages.ExtensionsPages.DevelopPages.Dialogs;

public sealed partial class AddReleaseResourceDialog : ContentDialog, IStyleable
{
    public AddReleaseResourceDialog()
    {
        InitializeComponent();
    }

    Type IStyleable.StyleKey => typeof(ContentDialog);
}
