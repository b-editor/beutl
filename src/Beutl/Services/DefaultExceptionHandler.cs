using Beutl.Api;

namespace Beutl.Services;

public static class DefaultExceptionHandler
{
    public static void Handle(this Exception exception)
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
