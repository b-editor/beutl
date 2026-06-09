using Beutl.Api.Clients;
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
        else if (exception is ProjectStateDivergedException diverged)
        {
            // Persist and rollback both failed; the editor and the saved file are out of sync. Ask
            // the user to reopen the project, and log the full chain for diagnosis.
            s_logger.LogError(diverged, "Project state diverged: persist and rollback both failed.");
            NotificationService.ShowError(Strings.Error, MessageStrings.ProjectStateDiverged);
        }
        else if (exception is not OperationCanceledException)
        {
            // Log as well as show: a toast is transient and dropped if no handler is registered, so
            // this keeps a durable trace.
            s_logger.LogError(exception, "Unhandled exception surfaced to the user.");
            NotificationService.ShowError(Strings.Error, exception.Message);
        }
    }
}
