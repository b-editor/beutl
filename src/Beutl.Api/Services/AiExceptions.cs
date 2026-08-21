namespace Beutl.Api.Services;

/// <summary>
/// Base class for errors reported by the server-side AI endpoints.
/// </summary>
public class AiException : Exception
{
    public AiException(
        string message,
        Exception? innerException = null,
        bool isTransient = false)
        : base(message, innerException)
    {
        IsTransient = isTransient;
    }

    public bool IsTransient { get; }
}

/// <summary>
/// The user is not signed in (401 authenticationIsRequired).
/// </summary>
public sealed class AuthenticationRequiredException : AiException
{
    public AuthenticationRequiredException(Exception? innerException = null)
        : base("Authentication is required.", innerException)
    {
    }
}

/// <summary>
/// The user does not have an active Pro plan (402 aiPlanRequired).
/// </summary>
public sealed class AiPlanRequiredException : AiException
{
    public AiPlanRequiredException(Exception? innerException = null)
        : base("A Pro plan is required to use AI features.", innerException)
    {
    }
}

/// <summary>
/// The user's monthly AI allowance and additional credits cannot cover the request
/// (402 aiUsageLimitExceeded).
/// </summary>
public sealed class AiUsageLimitExceededException : AiException
{
    public AiUsageLimitExceededException(Exception? innerException = null)
        : base("The request exceeds the remaining monthly AI allowance and additional credits.", innerException)
    {
    }
}

/// <summary>
/// The request names a model the server no longer offers for that operation.
/// Reruns hit this; falling back to the operation's default would run something
/// else and charge that model's price for it.
/// </summary>
public sealed class AiModelUnavailableException : AiException
{
    public AiModelUnavailableException(Exception? innerException = null)
        : base("The requested AI model is no longer offered for this operation.", innerException)
    {
    }
}

/// <summary>
/// The selected media exceeds the server upload limit (413 fileIsTooLarge).
/// </summary>
public sealed class AiFileTooLargeException : AiException
{
    public AiFileTooLargeException(Exception? innerException = null)
        : base("The selected file exceeds the AI upload limit.", innerException)
    {
    }
}

/// <summary>
/// The AI provider failed to process the request (500 aiProviderError).
/// </summary>
public sealed class AiProviderErrorException : AiException
{
    public AiProviderErrorException(Exception? innerException = null)
        : base("The AI provider failed to process the request.", innerException, isTransient: true)
    {
    }
}

/// <summary>
/// The user already has a video generation job in progress (429 aiJobLimitReached).
/// </summary>
public sealed class AiJobLimitReachedException : AiException
{
    public AiJobLimitReachedException(Exception? innerException = null)
        : base("A video generation job is already in progress.", innerException)
    {
    }
}

/// <summary>
/// A retry reused an idempotency key while the original request is still running
/// (409 aiRequestInProgress).
/// </summary>
public sealed class AiRequestInProgressException : AiException
{
    public AiRequestInProgressException(Exception? innerException = null)
        : base("The AI request is still in progress.", innerException, isTransient: true)
    {
    }
}

/// <summary>
/// A retry reused an idempotency key whose original request was deleted
/// (409 aiRequestWasDeleted).
/// </summary>
public sealed class AiRequestWasDeletedException : AiException
{
    public AiRequestWasDeletedException(Exception? innerException = null)
        : base("The original AI request was deleted. Start a new request to run it again.", innerException)
    {
    }
}

/// <summary>
/// The name this request was sent under belongs to a different request
/// (409 aiRequestChanged).
/// </summary>
/// <remarks>
/// The server fingerprints what a name was used for and refuses to answer it
/// with anything else. Asking again for exactly what that name was sent with
/// collects what it may already have paid for; anything else is a new request
/// and needs a new name.
/// </remarks>
public sealed class AiRequestChangedException : AiException
{
    public AiRequestChangedException(Exception? innerException = null)
        : base("The idempotency key belongs to a different request.", innerException)
    {
    }
}

/// <summary>
/// A paid result the server holds could not be read just now
/// (503 aiResultUnavailable).
/// </summary>
/// <remarks>
/// The job succeeded and was charged for; only fetching what it produced
/// failed. Nothing is settled by this, so asking again under the same
/// idempotency key is what recovers it — which is why it is told apart from a
/// failure the server owns and has refunded.
/// </remarks>
public sealed class AiResultUnavailableException : AiException
{
    public AiResultUnavailableException(Exception? innerException = null)
        : base(
            "The AI result could not be read. Ask for it again.",
            innerException,
            isTransient: true)
    {
    }
}

/// <summary>
/// The chosen model does not take the request as it stands — an aspect ratio,
/// a background, a seed or a reference picture it does not accept
/// (400 aiModelDoesNotSupportRequest).
/// </summary>
/// <remarks>
/// Refused before the operation is reserved, so nothing was charged. What the
/// caller has to change is the model or the shape of the request; repeating it
/// unchanged is refused the same way.
/// </remarks>
public sealed class AiModelDoesNotSupportRequestException : AiException
{
    public AiModelDoesNotSupportRequestException(Exception? innerException = null)
        : base("The chosen AI model does not support this request.", innerException)
    {
    }
}

/// <summary>
/// The connection carrying an answer ended before the answer did. The work may
/// well have finished and been charged for, so this is not the same as a run
/// that failed: asking again under the same idempotency key is what recovers it.
/// </summary>
public sealed class AiRequestInterruptedException : AiException
{
    public AiRequestInterruptedException(Exception? innerException = null)
        : base(
            "The AI answer was cut off before it finished.",
            innerException,
            isTransient: true)
    {
    }
}

/// <summary>
/// The job produced a result, but the file it produced could not be fetched.
/// The work is done and paid for either way, so it is worth saying so: the
/// result is still in the job history.
/// </summary>
public sealed class AiContentUnavailableException : AiException
{
    public AiContentUnavailableException(
        int statusCode,
        Exception? innerException = null)
        : base(
            $"The AI result could not be downloaded (HTTP {statusCode}).",
            innerException,
            isTransient: statusCode >= 500)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}

/// <summary>
/// The requested AI job is still active and cannot be deleted (409 aiJobIsActive).
/// </summary>
public sealed class AiJobIsActiveException : AiException
{
    public AiJobIsActiveException(Exception? innerException = null)
        : base("An active AI job cannot be deleted.", innerException)
    {
    }
}
