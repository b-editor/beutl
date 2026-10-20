using Beutl.Api.Services;
using Beutl.Language;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.Services;
using Beutl.Services.AI;
using Microsoft.Extensions.Logging;

namespace Beutl.ViewModels.Dialogs;

internal sealed partial class AiImageGenerationDialogViewModel
{
    private void OnIdentityChanged()
        => _recovery.SwitchIdentity(_identityOperations, ClearIdentityState);

    private void ClearIdentityState()
    {
        _runningRequest = null;
        IsGenerating.Value = false;
        _chosenAspectRatio = null;
        _chosenBackground = null;
        _chosenSeed = null;
        ClearReferenceImagesCore();
        Prompt.Value = string.Empty;
        Style.Value = string.Empty;
        Composition.Value = string.Empty;
        Exclusions.Value = string.Empty;
        ResultImage.Value?.Dispose();
        ResultImage.Value = null;
        PreviewImage.Value?.Dispose();
        PreviewImage.Value = null;
        Error.Value = null;
        ModelPicker.ReconcileRecoveryModels();
        ApplyModelCapabilities(ModelPicker.Selected.Value?.Model);
        _recovery.Revision.Value++;
    }

    // Looks a pending request's job up by its key and shows the picture it made.
    internal async Task CollectAsync(AiPendingAttempt attempt)
    {
        using IdentityOperationLifetime.Operation? operation = TryEnterIdentityOperation();
        if (operation is null
            || IsGenerating.Value
            || !operation.TryPublish(() =>
            {
                Error.Value = null;
                IsGenerating.Value = true;
            }))
        {
            return;
        }

        _runningRequest = operation;
        try
        {
            (AiPendingJobState state, AiJob? job) = await AiPendingJobLookup.FindAsync(
                _jobs, _jobKinds, _requestRecoveryContext, attempt, operation.CancellationToken);
            switch (state)
            {
                case AiPendingJobState.Succeeded:
                    if (job!.ContentUri is not { } contentUri)
                    {
                        operation.TryPublish(() => Error.Value = Strings.AiResultUnavailable);
                        break;
                    }

                    Ref<Bitmap> image = await AiImageResultDownload.DownloadBitmapAsync(
                        _content, contentUri, operation.CancellationToken);
                    _recovery.TrySettle(attempt);
                    if (!operation.TryPublish(() =>
                        {
                            ResultImage.Value?.Dispose();
                            ResultImage.Value = image;
                        }))
                    {
                        image.Dispose();
                    }
                    break;
                case AiPendingJobState.Running:
                    operation.TryPublish(() => Error.Value = Strings.AiRequestInProgress);
                    break;
                default:
                    _recovery.TrySettle(attempt);
                    operation.TryPublish(() => Error.Value = AiPendingJobLookup.MessageFor(state, job));
                    break;
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
            _logger.LogError(ex, "Failed to recover an AI image.");
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
        _recovery.Revision.Value++;
        // Reloads were held back while that name was outstanding, so this is
        // where an operator's change to the model list finally lands.
        _ = RefreshModelsAsync();
    }

    // A name the server never made a job under. Withdrawing it lets the picker
    // move again and puts the balance check back in front of the next attempt.
    private void WithdrawRequestName(AiRequestName name)
    {
        if (_requestKey.WithdrawAfterNoReservation(name))
            _recovery.Revision.Value++;
    }
}
