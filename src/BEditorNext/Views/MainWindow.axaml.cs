using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

using BEditorNext.Controls;
using BEditorNext.Pages;
using BEditorNext.Services;
using BEditorNext.ViewModels;
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
        SettingsPageItem.Tag = new SettingsPage();

        Navi.SelectedItem = EditPageItem;
        Navi.ItemInvoked += NavigationView_ItemInvoked;

        NaviContent.Content = EditPageItem.Tag;
#if DEBUG
        this.AttachDevTools();
#endif
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is MainWindowViewModel vm)
        {
            vm.CreateNewProject.Subscribe(async () =>
            {
                var dialog = new CreateNewProject();
                await dialog.ShowAsync();
            });

            vm.OpenProject.Subscribe(async () =>
            {
                ProjectService service = ServiceLocator.Current.GetRequiredService<ProjectService>();

                // Todo: Œã‚ÅŠg’£Žq‚ð•ÏX
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
            });

            vm.Exit.Subscribe(() =>
            {
                if (Application.Current.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime applicationLifetime)
                {
                    applicationLifetime.Shutdown();
                }
            });
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
