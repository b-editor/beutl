using Avalonia.Interactivity;
using Beutl.ViewModels.Tools;
using FluentAvalonia.UI.Controls;

namespace Beutl.Views.Dialogs;

public partial class OutputProgressDialog : ContentDialog
{
    public OutputProgressDialog()
    {
        InitializeComponent();
    }

    protected override Type StyleKeyOverride => typeof(ContentDialog);

    private void CancelClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is OutputViewModel vm)
        {
            vm.CancelEncode();
        }
        Hide();
    }

    private void OpenFolderClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is OutputViewModel vm)
        {
            vm.OpenContainingFolder();
        }
    }

    private void PlayClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is OutputViewModel vm)
        {
            vm.PlayOutput();
        }

        Hide();
    }

    private void CloseClick(object? sender, RoutedEventArgs e)
    {
        Hide();
    }
}
