using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Beutl.Api;

using Beutl.Controls.Navigation;
using Beutl.Extensibility.Services;

using Microsoft.Extensions.DependencyInjection;

namespace Beutl.ViewModels.ExtensionsPages;

public abstract class BasePageViewModel : PageContext, IDisposable
{
    protected BasePageViewModel()
    {
        Notification = ServiceLocator.Current.GetRequiredService<INotificationService>();
    }

    protected INotificationService Notification { get; }

    public abstract void Dispose();

    protected void ErrorHandle(Exception exception)
    {
        if (exception is BeutlApiException<ApiErrorResponse> apiError)
        {
            Notification.Show(new Notification(
                "API Error",
                apiError.Result.Message,
                NotificationType.Error));
        }
        else if (exception is BeutlApiException apiException)
        {
            Notification.Show(new Notification(
                "API Error",
                exception.Message,
                NotificationType.Error));
        }
        else if (exception is not OperationCanceledException)
        {
            Notification.Show(new Notification(
                "Error",
                exception.Message,
                NotificationType.Error));
        }
    }
}

public abstract class BaseViewModel : IDisposable
{
    private INotificationService? _notification;

    protected INotificationService Notification
        => _notification ??= ServiceLocator.Current.GetRequiredService<INotificationService>();

    public abstract void Dispose();

    protected void ErrorHandle(Exception exception)
    {
        if (exception is BeutlApiException<ApiErrorResponse> apiError)
        {
            Notification.Show(new Notification(
                "API Error",
                apiError.Result.Message,
                NotificationType.Error));
        }
        else if (exception is BeutlApiException apiException)
        {
            Notification.Show(new Notification(
                "API Error",
                exception.Message,
                NotificationType.Error));
        }
        else if (exception is not OperationCanceledException)
        {
            Notification.Show(new Notification(
                "Error",
                exception.Message,
                NotificationType.Error));
        }
    }
}
