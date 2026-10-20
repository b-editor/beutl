using Beutl.Api.Services;
using Beutl.Language;
using Beutl.Services;
using Beutl.Services.AI;
using Microsoft.Extensions.Logging;

namespace Beutl.ViewModels.Dialogs;

internal sealed partial class AiVideoGenerationDialogViewModel
{
    private void OnIdentityChanged()
        => _recovery.SwitchIdentity(_identityOperations, ClearIdentityState);

    private void ClearIdentityState()
    {
        _runningRequest = null;
        IsGenerating.Value = false;
        ClearVideoInputs();
        _chosenDuration = null;
        _chosenResolution = null;
        _chosenAspectRatio = null;
        _chosenAudio = true;
        _chosenSeed = null;
        SetFrameCore(isFirstFrame: true, null);
        SetFrameCore(isFirstFrame: false, null);
        Prompt.Value = string.Empty;
        Style.Value = string.Empty;
        Composition.Value = string.Empty;
        Motion.Value = string.Empty;
        Exclusions.Value = string.Empty;
        if (ResultVideoPath.Value is { } resultPath)
            RequestTemporaryFileDeletion(resultPath);
        ResultVideoPath.Value = null;
        _resultDurationSeconds = 0;
        StatusText.Value = InitialStatusText;
        Error.Value = null;
        ModelPicker.ReconcileRecoveryModels();
        ApplyModelCapabilities(ModelPicker.Selected.Value?.Model);
        _recovery.Revision.Value++;
    }

    // Looks a pending request's job up by its key, waits for it while it runs and shows the clip.
    internal async Task CollectAsync(AiPendingAttempt attempt)
    {
        using IdentityOperationLifetime.Operation? operation = TryEnterIdentityOperation();
        if (operation is null
            || IsGenerating.Value
            || !operation.TryPublish(() =>
            {
                Error.Value = null;
                IsGenerating.Value = true;
                StatusText.Value = Strings.AiVideoProcessing;
            }))
        {
            return;
        }

        _runningRequest = operation;
        try
        {
            (AiPendingJobState state, AiJob? job) = await AiPendingJobLookup.FindAsync(
                _jobs, _jobKinds, _requestRecoveryContext, attempt, operation.CancellationToken);
            if (state is AiPendingJobState.Succeeded or AiPendingJobState.Running)
            {
                using IDisposable scope = _requestRecoveryContext.Enter(attempt.AccountId);
                // The clip's length is read from the file, as the request that made it is not kept.
                if (await PollJobAsync(job!.Id, operation, resultDurationSeconds: null))
                    _recovery.TrySettle(attempt);
            }
            else
            {
                _recovery.TrySettle(attempt);
                operation.TryPublish(() =>
                {
                    StatusText.Value = InitialStatusText;
                    Error.Value = AiPendingJobLookup.MessageFor(state, job);
                });
            }
        }
        catch (Exception ex) when (AiRequestFailure.Classify(ex) is { } failure)
        {
            operation.TryPublish(() => Error.Value = failure.Message);
        }
        catch (OperationCanceledException) when (operation.CancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to recover an AI video.");
            operation.TryPublish(() => Error.Value = Strings.AiUnexpectedError);
        }
        finally
        {
            if (ReferenceEquals(_runningRequest, operation))
                _runningRequest = null;
            operation.TryPublish(() => IsGenerating.Value = false);
        }
    }

    internal void AbandonPendingAttempt(AiPendingAttempt attempt) => _recovery.Abandon(attempt);

    // Only this one request is settled. Retiring the whole run instead would
    // throw away the name of anything else still waiting to be collected.
    private void RetireRequestName(AiRequestName name)
    {
        if (!_requestKey.Retire(name))
            return;
        ReleaseFramesOf(name);
        _recovery.Revision.Value++;
        // Reloads were held back while that name was outstanding, so this is
        // where an operator's change to the model list finally lands.
        _ = RefreshModelsAsync();
    }

    // A name the server never made a job under. Withdrawing it lets the picker
    // move again and puts the balance check back in front of the next attempt.
    private void WithdrawRequestName(AiRequestName name)
    {
        if (!_requestKey.WithdrawAfterNoReservation(name))
            return;
        ReleaseFramesOf(name);
        _recovery.Revision.Value++;
    }
}
