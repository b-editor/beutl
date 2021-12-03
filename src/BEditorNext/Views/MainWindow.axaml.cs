using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

using BEditorNext.Controls;
using BEditorNext.Pages;
using BEditorNext.Services;
using BEditorNext.Views.Dialogs;

using FluentAvalonia.UI.Controls;

using Microsoft.Extensions.DependencyInjection;

namespace BEditorNext.Views;

public sealed partial class MainWindow : FluentWindow
{
    public MainWindow()
    {
        InitializeComponent();
        TitleBarArea.PointerPressed += TitleBarArea_PointerPressed;

        EditPageItem.Tag = new EditPage();

        Navi.SelectedItem = EditPageItem;
        Navi.ItemInvoked += NavigationView_ItemInvoked;

        NaviContent.Content = EditPageItem.Tag;
#if DEBUG
        this.AttachDevTools();
#endif
    }

    private async void CreateNewClicked(object? sender, RoutedEventArgs e)
    {
        var dialog = new CreateNewProject();
        await dialog.ShowAsync();
    }

    private async void OpenClicked(object? sender, RoutedEventArgs e)
    {
        ProjectService service = ServiceLocator.Current.GetRequiredService<ProjectService>();
        var dialog = new OpenFileDialog
        {
            Filters =
            {
                new FileDialogFilter
                {
                    Name = Application.Current.FindResource("ProjectFileString") as string,
                    Extensions =
                    {
                        "bep"
                    }
                }
            }
        };

        string[]? files = await dialog.ShowAsync(this);
        if ((files?.Any() ?? false) && File.Exists(files[0]))
        {
            service.OpenProject(files[0]);
        }
    }

    private void TitleBarArea_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        BeginMoveDrag(e);
    }

    private void NavigationView_ItemInvoked(object? sender, NavigationViewItemInvokedEventArgs e)
    {
        if (e.InvokedItemContainer is NavigationViewItem item)
        {
            NaviContent.Content = item.Tag;
            e.RecommendedNavigationTransitionInfo.RunAnimation(NaviContent);
        }
    }
}
