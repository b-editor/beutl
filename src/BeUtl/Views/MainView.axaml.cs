using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

using BeUtl.Pages;
using BeUtl.Services;
using BeUtl.ViewModels;
using BeUtl.Views.Dialogs;

using FluentAvalonia.Core.ApplicationModel;
using FluentAvalonia.UI.Controls;

using Microsoft.Extensions.DependencyInjection;

namespace BeUtl.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();

        // NavigationView‚ÌÝ’è
        EditPageItem.Tag = new EditPage();
        SettingsPageItem.Tag = new SettingsPage();

        Navi.SelectedItem = EditPageItem;
        Navi.ItemInvoked += NavigationView_ItemInvoked;

        NaviContent.Content = EditPageItem.Tag;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is MainViewModel vm)
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
                            Name = Application.Current?.FindResource("S.Common.ProjectFile") as string,
                            Extensions =
                            {
                                "bep"
                            }
                        }
                    }
                };

                if (VisualRoot is Window window)
                {
                    string[]? files = await dialog.ShowAsync(window);
                    if ((files?.Any() ?? false) && File.Exists(files[0]))
                    {
                        service.OpenProject(files[0]);
                    }
                }
            });

            vm.Exit.Subscribe(() =>
            {
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime applicationLifetime)
                {
                    applicationLifetime.Shutdown();
                }
            });
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (e.Root is Window b)
        {
            b.Opened += OnParentWindowOpened;
        }
    }

    private void OnParentWindowOpened(object? sender, EventArgs e)
    {
        if (sender is Window window)
        {
            window.Opened -= OnParentWindowOpened;
        }

        if (sender is CoreWindow cw)
        {
            CoreApplicationViewTitleBar titleBar = cw.TitleBar;
            if (titleBar != null)
            {
                titleBar.ExtendViewIntoTitleBar = true;

                titleBar.LayoutMetricsChanged += OnApplicationTitleBarLayoutMetricsChanged;

                cw.SetTitleBar(Titlebar);
                Titlebar.Margin = new Thickness(0, 0, titleBar.SystemOverlayRightInset, 0);
            }
        }
    }

    private void OnApplicationTitleBarLayoutMetricsChanged(CoreApplicationViewTitleBar sender, object args)
    {
        Titlebar.Margin = new Thickness(0, 0, sender.SystemOverlayRightInset, 0);
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
