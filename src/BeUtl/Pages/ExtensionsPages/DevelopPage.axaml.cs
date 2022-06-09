using Avalonia.Interactivity;
using Avalonia.Controls;
using BeUtl.ViewModels.ExtensionsPages;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using Avalonia.Input;
using BeUtl.ViewModels.ExtensionsPages.DevelopPages;
using BeUtl.Pages.ExtensionsPages.DevelopPages;

namespace BeUtl.Pages.ExtensionsPages;

public partial class DevelopPage : UserControl
{
    private bool flag;

    public DevelopPage()
    {
        InitializeComponent();
        packagesList.AddHandler(PointerPressedEvent, PackagesList_PointerPressed, RoutingStrategies.Tunnel);
        packagesList.AddHandler(PointerReleasedEvent, PackagesList_PointerReleased, RoutingStrategies.Tunnel);
    }

    private void PackagesList_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (flag)
        {
            if (packagesList.SelectedItem is PackagePageViewModel selectedItem)
            {
                Frame frame = this.FindAncestorOfType<Frame>();
                frame.Navigate(typeof(PackagePage), selectedItem);
            }
            flag = false;
        }
    }

    private void PackagesList_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            flag = true;
        }
    }

    private void CreateNewPackage_Click(object? sender, RoutedEventArgs e)
    {
        Frame frame = this.FindAncestorOfType<Frame>();
        if (DataContext is DevelopPageViewModel viewModel)
        {
            viewModel.CreateNewPackage.Execute(frame);
        }
    }
}
