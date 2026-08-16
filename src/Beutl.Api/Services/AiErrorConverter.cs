using System.Diagnostics;
using Beutl.Api.Clients;
using Refit;

namespace Beutl.Api.Services;

internal static class AiErrorConverter
{
    public static async Task<AiException> ConvertAsync(ApiException exception, Activity? activity)
    {
        activity?.SetStatus(ActivityStatusCode.Error);

        try
        {
            ApiErrorResponse? error = await exception.GetContentAsAsync<ApiErrorResponse>();
            return error?.ErrorCode switch
            {
                ApiErrorCode.AuthenticationIsRequired => new AuthenticationRequiredException(exception),
                ApiErrorCode.AiPlanRequired => new AiPlanRequiredException(exception),
                ApiErrorCode.AiUsageLimitExceeded => new AiUsageLimitExceededException(exception),
                ApiErrorCode.FileIsTooLarge => new AiFileTooLargeException(exception),
                ApiErrorCode.AiProviderError => new AiProviderErrorException(exception),
                ApiErrorCode.AiJobIsActive => new AiJobIsActiveException(exception),
                ApiErrorCode.AiJobLimitReached => new AiJobLimitReachedException(exception),
                ApiErrorCode.AiRequestInProgress => new AiRequestInProgressException(exception),
                ApiErrorCode.AiRequestWasDeleted => new AiRequestWasDeletedException(exception),
                _ => new AiException(
                    error?.Message ?? exception.Message,
                    exception,
                    isTransient: (int)exception.StatusCode >= 500),
            };
        }
        catch (Exception parseException)
        {
            var converted = new AiException(
                exception.Message,
                exception,
                isTransient: (int)exception.StatusCode >= 500);
            converted.Data[nameof(parseException)] = parseException;
            return converted;
        }
    }
}
