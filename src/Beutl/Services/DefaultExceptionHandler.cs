using Beutl.Api.Clients;
using Beutl.Language;
using Beutl.Logging;
using Microsoft.Extensions.Logging;
using Refit;

namespace Beutl.Services;

public static class DefaultExceptionHandler
{
    private static readonly ILogger s_logger = Log.CreateLogger(typeof(DefaultExceptionHandler));

    public static async ValueTask Handle(this Exception exception)
    {
        if (exception is ApiException apiError)
        {
            try
            {
                var err = await apiError.GetContentAsAsync<ApiErrorResponse>();
                NotificationService.ShowError(Strings.APIError, err?.Message ?? apiError.Message);
            }
            catch (Exception ex)
            {
                s_logger.LogError(ex, "Error while handling API error: {ApiContent}", apiError.Content);
                NotificationService.ShowError(Strings.APIError, apiError.Message);
            }
        }
        else if (exception is not OperationCanceledException)
        {
            // Record the failure as well as showing it: a toast is transient (and dropped entirely
            // if no notification handler is registered), so without this the error would leave no
            // durable trace for diagnosis.
            s_logger.LogError(exception, "Unhandled exception surfaced to the user.");
            NotificationService.ShowError(Strings.Error, exception.Message);
        }
    }
}
