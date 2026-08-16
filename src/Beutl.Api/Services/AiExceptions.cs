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
/// The requested AI job is still active and cannot be deleted (409 aiJobIsActive).
/// </summary>
public sealed class AiJobIsActiveException : AiException
{
    public AiJobIsActiveException(Exception? innerException = null)
        : base("An active AI job cannot be deleted.", innerException)
    {
    }
}
