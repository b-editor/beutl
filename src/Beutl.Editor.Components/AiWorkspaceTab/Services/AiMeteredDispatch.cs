using Beutl.Api.Services;

namespace Beutl.Editor.Components.AiWorkspaceTab.Services;

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
        AiAccountJobScope account,
        AiRequestName name,
        Func<CancellationToken, Task<bool>> checkAvailability,
        Func<CancellationToken, Task<T>> send,
        Action<AiRequestName> withdraw,
        CancellationToken cancellationToken)
    {
        bool sent = false;
        try
        {
            // Not for a repeat: the server looks up the job this name already
            // made before it looks at the balance, so refusing here would
            // refuse to collect something already paid for.
            if (!name.IsRepeat && !await account.SendAsync(checkAvailability, cancellationToken))
                throw new AiUsageLimitExceededException();

            using IDisposable scope = account.Enter(cancellationToken);
            sent = true;
            return await send(cancellationToken);
        }
        catch (Exception ex) when ((!sent && !name.IsRepeat)
            || (!account.AccountChanged && AiRequestOutcome.CanWithdraw(name, ex)))
        {
            withdraw(name);
            throw;
        }
    }
}
