using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Beutl.Api;

using BeUtl.Controls.Navigation;
using BeUtl.Framework.Service;

using Microsoft.Extensions.DependencyInjection;

namespace BeUtl.ViewModels.ExtensionsPages;

public abstract class BaseDevelopPageViewModel : PageContext, IDisposable
{
    protected BaseDevelopPageViewModel()
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

#pragma warning disable CA1822
    protected string NotNullOrWhitespace(string str)
#pragma warning restore CA1822
    {
        if (!string.IsNullOrWhiteSpace(str))
        {
            return null!;
        }
        else
        {
            return S.Message.PleaseEnterString;
        }
    }
}
