using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

using BeUtl.Pages;
using BeUtl.Services;
using BeUtl.ViewModels;
using BeUtl.Views.Dialogs;

using FluentAvalonia.UI.Controls;

using Microsoft.Extensions.DependencyInjection;

namespace BeUtl.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();

        // NavigationViewÇÃê›íË
        EditPageItem.Tag = new EditPage();
        SettingsPageItem.Tag = new SettingsPage();

        Navi.SelectedItem = EditPageItem;
        Navi.ItemInvoked += NavigationView_ItemInvoked;

        NaviContent.Content = EditPageItem.Tag;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        void PlayerAction(Action<PlayerViewModel> action)
        {
            if (NaviContent.Content is EditPage
                {
                    tabview:
                    {
                        SelectedContent: EditView
                        {
                            DataContext: EditViewModel
                            {
                                Player: PlayerViewModel player
                            }
                        }
                    }
                })
            {
                action(player);
            }
        }
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

                // Todo: å„Ç≈ägí£éqÇïœçX
                var dialog = new OpenFileDialog
                {
                    Filters =
                    {
                        new FileDialogFilter
                        {
                            Name = Application.Current?.FindResource("ProjectFileString") as string,
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

            vm.PlayPause.Subscribe(() => PlayerAction(p =>
            {
                if (p.IsPlaying.Value)
                {
                    p.Pause();
                }
                else
                {
                    p.Play();
                }
            }));

            vm.Next.Subscribe(() => PlayerAction(p => p.Next()));

            vm.Previous.Subscribe(() => PlayerAction(p => p.Previous()));

            vm.Start.Subscribe(() => PlayerAction(p => p.Start()));

            vm.End.Subscribe(() => PlayerAction(p => p.End()));
        }
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
