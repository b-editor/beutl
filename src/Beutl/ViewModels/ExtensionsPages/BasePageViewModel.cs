using Beutl.Api;

using Beutl.Controls.Navigation;
using Beutl.Services;

using Microsoft.Extensions.DependencyInjection;

namespace Beutl.ViewModels.ExtensionsPages;

public abstract class BasePageViewModel : PageContext, IDisposable
{
    public abstract void Dispose();

    protected void ErrorHandle(Exception exception)
    {
        if (exception is BeutlApiException<ApiErrorResponse> apiError)
        {
            NotificationService.ShowError("API Error", apiError.Result.Message);
        }
        else if (exception is BeutlApiException apiException)
        {
            NotificationService.ShowError("API Error", exception.Message);
        }
        else if (exception is not OperationCanceledException)
        {
            NotificationService.ShowError("Error", exception.Message);
        }
    }
}

public abstract class BaseViewModel : IDisposable
{
    public abstract void Dispose();

    protected void ErrorHandle(Exception exception)
    {
        if (exception is BeutlApiException<ApiErrorResponse> apiError)
        {
            NotificationService.ShowError("API Error", apiError.Result.Message);
        }
        else if (exception is BeutlApiException apiException)
        {
            NotificationService.ShowError("API Error", exception.Message);
        }
        else if (exception is not OperationCanceledException)
        {
            NotificationService.ShowError("Error", exception.Message);
        }
    }
}
