using Beutl.Api.Services;
using Beutl.Language;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.Services;
using Beutl.Services.AI;
using Microsoft.Extensions.Logging;

namespace Beutl.ViewModels.Dialogs;

internal sealed partial class AiImageEditDialogViewModel
{
    private void OnIdentityChanged()
        => _recovery.SwitchIdentity(_identityOperations, ClearIdentityState);

    private void ClearIdentityState()
    {
        _runningRequest = null;
        IsEditing.Value = false;
        SourceFilePath.Value = null;
        Prompt.Value = string.Empty;
        OriginalImage.Value?.Dispose();
        OriginalImage.Value = null;
        ResultImage.Value?.Dispose();
        ResultImage.Value = null;
        Error.Value = null;
        ModelPicker.ReconcileRecoveryModels();
        _recovery.Revision.Value++;
    }

    // Looks a pending request's job up by its key and shows the picture it made.
    internal async Task CollectAsync(AiPendingAttempt attempt)
    {
        using IdentityOperationLifetime.Operation? operation = TryEnterIdentityOperation();
        if (operation is null
            || IsEditing.Value
            || !operation.TryPublish(() =>
            {
                Error.Value = null;
                IsEditing.Value = true;
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
                            SelectedComparisonMode.Value = ComparisonModes[0];
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
            _logger.LogError(ex, "Failed to recover an AI edited image.");
            operation.TryPublish(() => Error.Value = Strings.AiUnexpectedError);
        }
        finally
        {
            if (ReferenceEquals(_runningRequest, operation))
                _runningRequest = null;
            operation.TryPublish(() => IsEditing.Value = false);
        }
    }

    internal void AbandonPendingAttempt(AiPendingAttempt attempt) => _recovery.Abandon(attempt);

    // Only this one request is settled. Retiring the whole run instead would
    // throw away the name of anything else still waiting to be collected.
    private void RetireRequestName(AiRequestName name)
    {
        if (!_requestKey.Retire(name))
            return;
        Forget(name);
        // Reloads were held back while that name was outstanding, so this is
        // where an operator's change to the model list finally lands.
        _ = ReloadModelsAsync(SelectedTask.Value);
    }

    // A name the server never made a job under. Withdrawing it lets the picker
    // move again and puts the balance check back in front of the next attempt.
    private void WithdrawRequestName(AiRequestName name)
    {
        if (_requestKey.WithdrawAfterNoReservation(name))
            Forget(name);
    }

    private void Forget(AiRequestName name)
    {
        _outstanding.Forget(name);
        _outstandingRevision.Value++;
        _recovery.Revision.Value++;
    }

    // Whether any request this tab sent is still waiting to be collected for this task.
    // Each of the five is its own operation with its own models and its own
    // price, so a name outstanding on one says nothing about another.
    private bool HoldsNameFor(string task)
        => _outstanding.Any(request => IsFor(request, task));

    private bool TryFindOutstandingModelFor(string task, out AiModelId? model)
    {
        model = null;
        if (!_outstanding.TryFind(request => IsFor(request, task), out string?[] held))
            return false;
        model = held[ModelPartIndex] is { } id ? new AiModelId(id) : null;
        return true;
    }

    private IReadOnlyList<AiModelId> ModelsOfOutstandingRequestsFor(AiOperationId operation)
        => _outstanding.All()
            .Where(request => request[TaskPartIndex] is { } task
                && AiOperations.ImageEdit(new AiImageEditTaskId(task)) == operation)
            .Select(request => request[ModelPartIndex])
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .Select(model => new AiModelId(model))
            .ToArray();

    private static bool IsFor(string?[] request, string task)
        => string.Equals(request[TaskPartIndex], task, StringComparison.Ordinal);
}
