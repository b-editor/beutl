using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Beutl.Logging;
using Beutl.Views;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.Logging;

namespace Beutl.Services;

public sealed class NotificationServiceHandler : INotificationServiceHandler
{
    private readonly ILogger _logger = Log.CreateLogger<NotificationServiceHandler>();

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
        // ShowCoreAsync 側の Expiration 待機が後から `if (!infoBar.IsOpen) return;` を
        // 通過して HiddenNotificationPanel に積み直さないように、ここで明示的に閉じる
        infoBar.IsOpen = false;
        if (GetMainView() is MainView mainView)
        {
            mainView.NotificationPanel.Children.Remove(infoBar);
            mainView.HiddenNotificationPanel.Children.Remove(infoBar);
        }
    }

    public void Show(Notification notification)
    {
        _ = ShowCoreAsync(notification);
    }

    private async Task ShowCoreAsync(Notification notification)
    {
        try
        {
            await App.WaitWindowOpened();

            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                try
                {
                    if (GetMainView() is not MainView mainView)
                        return;

                    InfoBar infoBar = BuildInfoBar(notification);
                    mainView.NotificationPanel.Children.Add(infoBar);

                    await Task.Delay(notification.Expiration ?? TimeSpan.FromSeconds(3));

                    if (infoBar.IsPointerOver)
                        await WaitPointerExitedAsync(infoBar);

                    if (!infoBar.IsOpen)
                        return;

                    infoBar.IsOpen = false;
                    // FluentAvalonia の InfoBar クローズアニメーション完了待ち (≈167ms)
                    await Task.Delay(167);

                    if (GetMainView() is MainView mv)
                    {
                        mv.NotificationPanel.Children.Remove(infoBar);
                        mv.HiddenNotificationPanel.Children.Add(infoBar);
                        infoBar.IsOpen = true;
                    }
                }
                catch (Exception e)
                {
                    _logger.LogError(
                        e,
                        "Failed to show notification (Type={Type}, Title={Title})",
                        notification.Type, notification.Title);
                }
            });
        }
        // dispatcher shutdown 等、InvokeAsync 自体の失敗をここで握る
        catch (Exception e)
        {
            _logger.LogError(
                e,
                "Failed to dispatch notification (Type={Type}, Title={Title})",
                notification.Type, notification.Title);
        }
    }

    private InfoBar BuildInfoBar(Notification notification)
    {
        var infoBar = new InfoBar
        {
            [!TemplatedControl.BackgroundProperty] =
                new DynamicResourceExtension("SolidBackgroundFillColorTertiaryBrush"),
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
            if (s is InfoBar { DataContext: Notification n } closingBar)
            {
                InvokeCallback(n.OnClose, n, "OnClose");
                Close(closingBar);
            }
        };

        if (notification.OnActionButtonClick != null)
        {
            var actionButton = new Button { Content = notification.ActionButtonText ?? "Action" };
            actionButton.Click += (s, _) =>
            {
                if (s is Button { DataContext: Notification n } button)
                {
                    InvokeCallback(n.OnActionButtonClick, n, "OnActionButtonClick");

                    InfoBar? ancestor = button.FindLogicalAncestorOfType<InfoBar>();
                    if (ancestor != null)
                        Close(ancestor);
                }
            };
            infoBar.ActionButton = actionButton;
        }

        return infoBar;
    }

    private void InvokeCallback(Action? callback, Notification notification, string callbackName)
    {
        if (callback is null) return;
        try
        {
            callback();
        }
        // 呼び出し元が任意の delegate を渡せるため、ここで握ってログに残さないと
        // Avalonia の global handler 経由でクラッシュしうる
        catch (Exception e)
        {
            _logger.LogError(
                e,
                "Notification {Callback} threw (Type={Type}, Title={Title})",
                callbackName, notification.Type, notification.Title);
        }
    }

    private static Task WaitPointerExitedAsync(InfoBar infoBar)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        void Cleanup()
        {
            infoBar.PointerExited -= OnExited;
            infoBar.DetachedFromVisualTree -= OnDetached;
        }

        void OnExited(object? sender, PointerEventArgs e)
        {
            Cleanup();
            tcs.TrySetResult();
        }

        // Close ボタンや ActionButton で infoBar がツリーから外された場合、
        // PointerExited が発火しないことがあるため detach でも解放する
        void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
        {
            Cleanup();
            tcs.TrySetResult();
        }

        infoBar.PointerExited += OnExited;
        infoBar.DetachedFromVisualTree += OnDetached;

        // 購読前にカーソルが既に外れていた／ツリーから外れていた取りこぼしを補償
        if (!infoBar.IsPointerOver || !infoBar.IsAttachedToVisualTree())
        {
            Cleanup();
            tcs.TrySetResult();
        }

        return tcs.Task;
    }
}
