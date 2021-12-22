using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

using BEditorNext.Framework.Service;
using BEditorNext.Views;

namespace BEditorNext.Services;

public sealed class NotificationService : INotificationService
{
    public void Show(Notification notification)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime applicationLifetime &&
                applicationLifetime.MainWindow is MainWindow window)
            {
                window.NotificationManager.Show(new Avalonia.Controls.Notifications.Notification(
                    notification.Title, notification.Message,
                    (Avalonia.Controls.Notifications.NotificationType)notification.Type,
                    notification.Expiration,
                    notification.OnClick,
                    notification.OnClose));
            }
        });
    }
}
