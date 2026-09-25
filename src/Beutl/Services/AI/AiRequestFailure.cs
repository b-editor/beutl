using Beutl.Api.Services;
using Beutl.Language;

namespace Beutl.Services.AI;

/// <summary>
/// What a failed metered request means to the person who asked for it, and
/// whether the name it went out under is spent.
/// </summary>
/// <param name="Message">The sentence to show.</param>
/// <param name="RetiresName">
/// The server settled the job (failed and refunded, or deleted), so its name
/// would keep answering with that and the next attempt needs a new one.
/// </param>
/// <param name="IsResultDownloadFailure">
/// The job ran and was charged for; only fetching what it produced failed.
/// </param>
internal sealed record AiRequestFailure(string Message, bool RetiresName, bool IsResultDownloadFailure = false)
{
    /// <summary>
    /// The known refusals of a metered request, or null for anything else —
    /// cancellation included, which each caller treats as its own.
    /// </summary>
    public static AiRequestFailure? Classify(Exception exception)
    {
        return exception switch
        {
            // Refused before anything was reserved; these only say what happened.
            AuthenticationRequiredException => new(Strings.AiAuthenticationRequired, false),
            AiPlanRequiredException => new(Strings.AiProRequired, false),
            AiUsageLimitExceededException => new(Strings.AiUsageLimitExceeded, false),
            AiFileTooLargeException => new(Strings.AiFileTooLarge, false),
            AiModelUnavailableException => new(Strings.AiModelUnavailable, false),
            // What has to change is the model or the shape of the request.
            AiModelDoesNotSupportRequestException => new(Strings.AiModelDoesNotSupportRequest, false),
            AiJobLimitReachedException => new(Strings.AiVideoJobLimitReached, false),
            // Charged for and still the server's; asking again under the same
            // name is what recovers it, so the key stays.
            AiResultUnavailableException => new(Strings.AiResultUnavailable, false),
            // A request keeps its name across attempts, and until the job it
            // made finishes this is the answer. The key is still the way back.
            AiRequestInProgressException => new(Strings.AiRequestInProgress, false),
            // The name belongs to a different request. Keeping it lets restoring
            // the form resend that request and reach an already-paid job.
            AiRequestChangedException => new(Strings.AiRequestChanged, false),
            // Settled server-side: the name would only ever answer with this.
            AiProviderErrorException => new(Strings.AiProviderError, true),
            AiRequestWasDeletedException => new(Strings.AiRequestWasDeleted, true),
            AiJobNotFoundException => new(Strings.AiRequestWasDeleted, true),
            AiContentUnavailableException => new(Strings.AiResultDownloadFailed, false, true),
            _ => null,
        };
    }
}
