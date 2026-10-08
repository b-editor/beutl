using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
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
    private readonly Func<MainView?> _findMainView;

    public NotificationServiceHandler() : this(MainViewLocator.Find) { }

    internal NotificationServiceHandler(Func<MainView?> findMainView)
    {
        _findMainView = findMainView;
    }

    private void Close(FAInfoBar infoBar)
    {
        // ShowCoreAsync 側の Expiration 待機が後から `if (!infoBar.IsOpen) return;` を
        // 通過して HiddenNotificationPanel に積み直さないように、ここで明示的に閉じる
        infoBar.IsOpen = false;
        if (_findMainView() is MainView mainView)
        {
            mainView.NotificationPanel.Children.Remove(infoBar);
            mainView.HiddenNotificationPanel.Children.Remove(infoBar);
        }
    }

    public void Show(Notification notification)
    {
        _ = ShowCoreAsync(notification);
    }

    internal async Task ShowCoreAsync(Notification notification)
    {
        Action closeNotification = CreateOnceCallback(
            () => InvokeCallback(notification.OnClose, notification, "OnClose"));
        Action showFailed = CreateOnceCallback(
            () => InvokeCallback(notification.OnShowFailed, notification, "OnShowFailed"));
        bool shown = false;

        try
        {
            await App.WaitWindowOpened().AsTask().WaitAsync(notification.CancellationToken);

            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                try
                {
                    notification.CancellationToken.ThrowIfCancellationRequested();
                    if (_findMainView() is not MainView mainView)
                    {
                        showFailed();
                        return;
                    }

                    var dismissed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    FAInfoBar infoBar = BuildInfoBar(notification, dismissed, closeNotification);
                    mainView.NotificationPanel.Children.Add(infoBar);
                    shown = true;
                    using CancellationTokenRegistration registration = notification.CancellationToken.Register(
                        () => Dispatcher.UIThread.Post(() =>
                        {
                            Close(infoBar);
                            dismissed.TrySetResult();
                        }));

                    if (await WaitForDismissal(
                            notification.Expiration ?? TimeSpan.FromSeconds(3), dismissed.Task))
                        return;

                    if (infoBar.IsPointerOver)
                        await WaitPointerExitedAsync(infoBar);

                    if (!infoBar.IsOpen)
                        return;

                    infoBar.IsOpen = false;
                    // FluentAvalonia の FAInfoBar クローズアニメーション完了待ち (≈167ms)
                    await Task.Delay(167);
                    if (dismissed.Task.IsCompleted) return;

                    if (_findMainView() is MainView mv)
                    {
                        mv.NotificationPanel.Children.Remove(infoBar);
                        mv.HiddenNotificationPanel.Children.Add(infoBar);
                        infoBar.IsOpen = true;
                    }

                    if (notification.CancellationToken.CanBeCanceled)
                        await dismissed.Task;
                }
                catch (OperationCanceledException) when (notification.CancellationToken.IsCancellationRequested)
                {
                }
                catch (Exception e)
                {
                    _logger.LogError(
                        e,
                        "Failed to show notification (Type={Type}, Title={Title})",
                        notification.Type, notification.Title);
                    if (!shown)
                    {
                        showFailed();
                    }
                }
            });
        }
        catch (OperationCanceledException) when (notification.CancellationToken.IsCancellationRequested)
        {
        }
        // dispatcher shutdown 等、InvokeAsync 自体の失敗をここで握る
        catch (Exception e)
        {
            _logger.LogError(
                e,
                "Failed to dispatch notification (Type={Type}, Title={Title})",
                notification.Type, notification.Title);
            if (!shown)
            {
                showFailed();
            }
        }
    }

    internal FAInfoBar BuildInfoBar(
        Notification notification,
        TaskCompletionSource dismissed,
        Action closeNotification)
    {
        var infoBar = new FAInfoBar
        {
            [!TemplatedControl.BackgroundProperty] =
                new DynamicResourceExtension("SolidBackgroundFillColorTertiaryBrush"),
            DataContext = notification,
            Title = notification.Title,
            Message = notification.Message,
            IsClosable = notification.IsClosable,
            IsOpen = true,
            Width = 350,
            Severity = notification.Type switch
            {
                NotificationType.Success => FAInfoBarSeverity.Success,
                NotificationType.Warning => FAInfoBarSeverity.Warning,
                NotificationType.Error => FAInfoBarSeverity.Error,
                NotificationType.Information or _ => FAInfoBarSeverity.Informational,
            }
        };

        infoBar.CloseButtonClick += (s, _) =>
        {
            if (s is FAInfoBar { DataContext: Notification } closingBar)
            {
                closeNotification();
                Close(closingBar);
                dismissed.TrySetResult();
            }
        };

        if (notification.Actions is { Count: > 0 } actions)
        {
            var actionPanel = new WrapPanel();
            foreach (NotificationAction action in actions)
            {
                var actionButton = new Button
                {
                    Content = action.Text,
                    Margin = new Thickness(4)
                };
                actionButton.Click += (_, _) =>
                {
                    InvokeCallback(action.Callback, notification, "Action");

                    if (action.DismissOnInvoke)
                    {
                        Close(infoBar);
                        dismissed.TrySetResult();
                    }
                };
                actionPanel.Children.Add(actionButton);
            }

            infoBar.ActionButton = actionPanel;
        }

        return infoBar;
    }

    internal static Action CreateOnceCallback(Action callback)
    {
        int invoked = 0;
        return () =>
        {
            if (Interlocked.Exchange(ref invoked, 1) == 0)
            {
                callback();
            }
        };
    }

    internal static async Task<bool> WaitForDismissal(TimeSpan expiration, Task dismissed)
    {
        using var cancellation = new CancellationTokenSource();
        Task expirationTask = Task.Delay(expiration, cancellation.Token);
        if (await Task.WhenAny(expirationTask, dismissed) == dismissed)
        {
            await cancellation.CancelAsync();
            return true;
        }

        return false;
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

    private static Task WaitPointerExitedAsync(FAInfoBar infoBar)
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
