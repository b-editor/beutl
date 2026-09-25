using Beutl.Api.Services;

namespace Beutl.Services.AI;

/// <summary>
/// Sends one named metered request: the balance check in front of it and the
/// withdrawal of a name the server never made a job under.
/// </summary>
/// <remarks>
/// Every screen and node that spends AI usage goes through here, so what counts
/// as "reached nothing" is decided in one place. Past the send the request may
/// have been paid for, and the name is the caller's to retire or keep.
/// </remarks>
internal static class AiMeteredDispatch
{
    public static async Task<T> SendAsync<T>(
        AiRequestKey requestKey,
        AiRequestName name,
        AiRequestRecoveryLease? claim,
        Func<CancellationToken, Task<bool>> checkAvailability,
        Func<CancellationToken, Task<T>> send,
        Action<AiRequestName> withdraw,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requestKey);
        ArgumentNullException.ThrowIfNull(checkAvailability);
        ArgumentNullException.ThrowIfNull(send);
        ArgumentNullException.ThrowIfNull(withdraw);

        // Before it goes out. A name that ends here reached nothing.
        try
        {
            // Not for a repeat: the server looks up the job this name already
            // made before it looks at the balance, so refusing here would
            // refuse to collect something already paid for.
            if (!name.IsRepeat && !await checkAvailability(cancellationToken))
                throw new AiUsageLimitExceededException();
        }
        catch
        {
            withdraw(name);
            throw;
        }

        try
        {
            requestKey.MarkClaimDispatched(claim);
            return await send(cancellationToken);
        }
        catch (Exception ex) when (AiRequestOutcome.CanWithdraw(name, ex))
        {
            withdraw(name);
            throw;
        }
    }
}
