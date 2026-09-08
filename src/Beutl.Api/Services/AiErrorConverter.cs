using System.Diagnostics;
using Beutl.Api.Clients;
using Refit;

namespace Beutl.Api.Services;

internal static class AiErrorConverter
{
    public static async Task<AiException> ConvertAsync(ApiException exception, Activity? activity)
    {
        activity?.SetStatus(ActivityStatusCode.Error);

        // The outer worker rejects an oversized streamed body before an endpoint can
        // produce its JSON error envelope. The HTTP status is authoritative even when
        // the response body is empty or malformed.
        if ((int)exception.StatusCode == 413)
            return new AiFileTooLargeException(exception);

        try
        {
            ApiErrorResponse? error = await exception.GetContentAsAsync<ApiErrorResponse>();
            return Convert(
                (int)exception.StatusCode,
                error,
                exception,
                exception.Message);
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

    /// <summary>
    /// The same failure, told apart the same way, for a reply that did not come
    /// back through Refit — an event stream reads its own error body.
    /// </summary>
    public static AiException Convert(
        int statusCode,
        ApiErrorResponse? error,
        Exception? innerException,
        string? fallbackMessage = null)
        => statusCode == 413
            ? new AiFileTooLargeException(innerException)
            : error?.ErrorCode switch
            {
                ApiErrorCode.AuthenticationIsRequired => new AuthenticationRequiredException(innerException),
                ApiErrorCode.AiPlanRequired => new AiPlanRequiredException(innerException),
                ApiErrorCode.AiUsageLimitExceeded => new AiUsageLimitExceededException(innerException),
                ApiErrorCode.FileIsTooLarge => new AiFileTooLargeException(innerException),
                ApiErrorCode.AiProviderError => new AiProviderErrorException(innerException),
                ApiErrorCode.AiJobNotFound => new AiJobNotFoundException(innerException),
                ApiErrorCode.AiJobIsActive => new AiJobIsActiveException(innerException),
                ApiErrorCode.AiJobLimitReached => new AiJobLimitReachedException(innerException),
                ApiErrorCode.AiRequestInProgress => new AiRequestInProgressException(innerException),
                ApiErrorCode.AiRequestWasDeleted => new AiRequestWasDeletedException(innerException),
                ApiErrorCode.AiRequestChanged => new AiRequestChangedException(innerException),
                ApiErrorCode.AiModelDoesNotSupportRequest =>
                    new AiModelDoesNotSupportRequestException(innerException),
                ApiErrorCode.AiModelUnavailable => new AiModelUnavailableException(innerException),
                ApiErrorCode.AiResultUnavailable => new AiResultUnavailableException(innerException),
                _ => new AiException(
                    error?.Message ?? fallbackMessage ?? "The AI request failed.",
                    innerException,
                    isTransient: statusCode >= 500),
            };
}
