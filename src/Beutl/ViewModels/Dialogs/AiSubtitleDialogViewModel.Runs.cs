using Beutl.Api.Services;
using Beutl.Services.AI;

namespace Beutl.ViewModels.Dialogs;

public sealed partial class AiSubtitleDialogViewModel
{
    // Hands out the name of one piece of a run before it goes out. The balance is checked first, but
    // not for a repeat: the server looks up the job a repeated name already made before it looks at the
    // balance, so refusing here would refuse to collect a piece already paid for. The run is then
    // written down before the piece goes out, not after it comes back. The first piece is the one most
    // likely to be charged and lost, and without this the name that charged it would die with the
    // session: the next run would name the same piece differently and buy it again. A run that cannot
    // be written down is not started at all, and false is returned. Anything that ends here ends before
    // the request went out, so the name reached nothing and is withdrawn.
    private async Task<bool> RecordRunPieceAsync<TOperation>(
        TOperation operation,
        AiRequestName name,
        Func<AiOperationAvailabilityRequest> availability,
        Func<TOperation, CaptionDraftOutcome> publish,
        Action<TOperation, AiRequestName> withdraw)
        where TOperation : CaptionRunOperation
    {
        try
        {
            if (!name.IsRepeat)
            {
                await EnsureAvailableAsync(availability());
            }

            return CanRunContinue(publish(operation));
        }
        catch
        {
            withdraw(operation, name);
            throw;
        }
    }

    // Sends a recorded piece and settles its name. A failed or invalid settled response needs a fresh
    // key on retry: its key would keep answering with that failure, so the rest of the run takes new
    // ones, written down as well, or a resumed run would ask under the spent key again. The durable key
    // is settled before any local progress is published; Settled is false when another owner replaced
    // the recovery row, and the operation is left untouched then so it can still be resumed.
    private async Task<(bool Settled, TResponse Response)> SendRunPieceAsync<TOperation, TResponse>(
        TOperation operation,
        AiRequestName name,
        Func<Task<TResponse>> send,
        Func<TOperation, CaptionDraftOutcome> publish,
        Action<TOperation, AiRequestName> withdraw)
        where TOperation : CaptionRunOperation
    {
        TResponse response;
        try
        {
            response = await send();
        }
        catch (AiProviderErrorException)
        {
            if (operation.RequestKey.Retire())
            {
                UpdateOutstandingCaptionRequest();
                publish(operation);
            }
            throw;
        }
        catch (Exception ex) when (AiRequestOutcome.CanWithdraw(name, ex))
        {
            withdraw(operation, name);
            throw;
        }

        return (operation.RequestKey.Retire(name), response);
    }

    // What every caption run has: the names its pieces go out under and the revisions it started at.
    private abstract class CaptionRunOperation(
        long expectedCaptionRevision,
        long expectedDraftScopeRevision,
        string? requestKeySeed,
        bool requestKeyNamePending)
    {
        /// <summary>
        /// Names this run's requests, one key per piece. Held with the rest of the resume state so a
        /// piece asked for again, after a restart too, gets the result already paid for instead of
        /// buying a second one.
        /// </summary>
        public AiRequestKey RequestKey { get; } = new(requestKeySeed, requestKeyNamePending);

        public string RequestKeySeed => RequestKey.Seed;

        /// <summary>The model the names handed out so far were built from.</summary>
        public string RequestKeyModel { get; set; } = string.Empty;

        public long ExpectedCaptionRevision { get; set; } = expectedCaptionRevision;

        public long ExpectedDraftScopeRevision { get; } = expectedDraftScopeRevision;

        // The model belongs in the key because the server refuses a key that comes back with a
        // different request behind it, and it fingerprints the model. A piece sent to another model
        // is another request and takes another key, which is also what it costs.
        public AiRequestName RequestNameFor(int index, AiModelId? model)
        {
            RequestKeyModel = model?.Value ?? string.Empty;
            return NameFor(index, model?.Value);
        }

        protected virtual AiRequestName NameFor(int index, string? model)
            => RequestKey.NameFor(index, model);
    }
}
