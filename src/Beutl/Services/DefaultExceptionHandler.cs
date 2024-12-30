using Beutl.Api.Clients;
using Refit;

namespace Beutl.Services;

public static class DefaultExceptionHandler
{
    public static async ValueTask Handle(this Exception exception)
    {
        if (exception is ApiException apiError)
        {
            var err = await apiError.GetContentAsAsync<ApiErrorResponse>();
            NotificationService.ShowError("API Error", err?.Message ?? apiError.Message);
        }
        else if (exception is not OperationCanceledException)
        {
            NotificationService.ShowError("Error", exception.Message);
        }
    }
}
