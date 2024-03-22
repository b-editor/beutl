using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Threading;

using Beutl.Views;

using FluentAvalonia.UI.Controls;

namespace Beutl.Services;

public sealed class NotificationServiceHandler : INotificationServiceHandler
{
    private static MainView? GetMainView()
    {
        IApplicationLifetime? lifetime = Application.Current?.ApplicationLifetime;

        if (lifetime is IClassicDesktopStyleApplicationLifetime desktopLifetime)
        {
            if (desktopLifetime.MainWindow is MainWindow window)
            {
                return window.mainView;
            }
            else if (desktopLifetime.MainWindow is MacWindow mwindow)
            {
                return mwindow.mainView;
            }
        }
        else if (lifetime is ISingleViewApplicationLifetime singleViewLifetime)
        {
            return singleViewLifetime.MainView as MainView;
        }
        return null;
    }

    private static void Close(InfoBar infoBar)
    {
        if (GetMainView() is MainView mainView)
        {
            mainView.NotificationPanel.Children.Remove(infoBar);
            mainView.HiddenNotificationPanel.Children.Remove(infoBar);
        }
    }

    public async void Show(Notification notification)
    {
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await App.WaitWindowOpened();

            if (GetMainView() is MainView mainView)
            {
                var infoBar = new InfoBar
                {
                    [!TemplatedControl.BackgroundProperty] = new DynamicResourceExtension("SolidBackgroundFillColorTertiaryBrush"),
                    DataContext = notification,
                    Title = notification.Title,
                    Message = notification.Message,
                    IsClosable = true,
                    IsOpen = true,
                    Width = 350,
                    Severity = notification.Type switch
                    {
                        NotificationType.Success => InfoBarSeverity.Success,
                        NotificationType.Warning => InfoBarSeverity.Warning,
                        NotificationType.Error => InfoBarSeverity.Error,
                        NotificationType.Information or _ => InfoBarSeverity.Informational,
                    }
                };

                infoBar.CloseButtonClick += (s, _) =>
                {
                    if (s is InfoBar { DataContext: Notification n } infoBar)
                    {
                        n.OnClose?.Invoke();
                        Close(infoBar);
                    }
                };

                if (notification.OnActionButtonClick != null)
                {
                    var actionButton = new Button
                    {
                        Content = notification.ActionButtonText ?? "Action"
                    };
                    actionButton.Click += (s, _) =>
                    {
                        if (s is Button { DataContext: Notification n } button)
                        {
                            n.OnActionButtonClick?.Invoke();

                            InfoBar? infoBar = button.FindLogicalAncestorOfType<InfoBar>();
                            if (infoBar != null)
                                Close(infoBar);
                        }
                    };
                    infoBar.ActionButton = actionButton;
                }

                mainView.NotificationPanel.Children.Add(infoBar);
                await Task.Delay(notification.Expiration ?? TimeSpan.FromSeconds(3));
                while (infoBar.IsPointerOver)
                {
                    await Task.Delay(3000);
                }

                if (infoBar.IsOpen)
                {
                    infoBar.IsOpen = false;
                    // アニメーション待ち
                    await Task.Delay(167);

                    mainView.NotificationPanel.Children.Remove(infoBar);
                    mainView.HiddenNotificationPanel.Children.Add(infoBar);
                    infoBar.IsOpen = true;
                }
            }
        });
    }
}
