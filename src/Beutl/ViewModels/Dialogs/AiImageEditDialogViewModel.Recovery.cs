using Beutl.Api.Services;
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
        _recovery.Clear();
        SourceFilePath.Value = null;
        Prompt.Value = string.Empty;
        _sourceElementId = null;
        OriginalImage.Value?.Dispose();
        OriginalImage.Value = null;
        ResultImage.Value?.Dispose();
        ResultImage.Value = null;
        Error.Value = null;
        ModelPicker.ReconcileRecoveryModels();
        _recovery.Revision.Value++;
    }

    internal bool TryRecoverPendingAttempt(AiPendingAttempt attempt)
    {
        if (_requestKey.CurrentAccountId is not { } account
            || !StringComparer.Ordinal.Equals(account, attempt.AccountId))
        {
            Error.Value = Strings.AiAuthenticationRequired;
            return false;
        }
        if (!attempt.HasCanonicalForm
            || attempt.Form?.Task is not { } task
            || !string.Equals(attempt.Operation, $"image.edit.{task}", StringComparison.Ordinal))
        {
            Error.Value = Strings.AiResultUnavailable;
            return false;
        }

        IReadOnlyList<string> paths;
        try
        {
            paths = _requestKey.ResolveSources(attempt);
        }
        catch (InvalidDataException ex)
        {
            _logger.LogWarning(ex, "Image-edit recovery source is unavailable.");
            Error.Value = Strings.AiResultUnavailable;
            return false;
        }

        if (attempt.EffectiveSources.Count != 1)
        {
            Error.Value = Strings.AiResultUnavailable;
            return false;
        }

        AiImageEditTaskOption? taskOption = Tasks.FirstOrDefault(option => option.Value == task);
        if (taskOption is null)
        {
            Error.Value = Strings.AiResultUnavailable;
            return false;
        }

        SelectedTask.Value = taskOption;
        Prompt.Value = attempt.Form!.Prompt ?? string.Empty;
        if (attempt.Form.OutpaintExpansionPercent is { } percent
            && OutpaintExpansionOptions.FirstOrDefault(option => option.Percent == percent) is { } expansion)
            SelectedOutpaintExpansion.Value = expansion;
        _sourceElementId = attempt.Form.SourceElementId;

        SourceFilePath.Value = paths[0];
        _recovery.Activate(attempt);
        _recovery.Revision.Value++;
        _recovery.SelectRecoveredModel();
        return true;
    }

    internal void AbandonPendingAttempt(AiPendingAttempt attempt) => _recovery.Abandon(attempt);

    // Only this one request is settled. Retiring the whole run instead would
    // throw away the name of anything else still waiting to be collected.
    private void RetireRequestName(AiRequestName name)
    {
        if (!_requestKey.Retire(name))
            return;
        ForgetSettled(name);
        // Reloads were held back while that name was outstanding, so this is
        // where an operator's change to the model list finally lands.
        _ = ReloadModelsAsync(SelectedTask.Value);
    }

    // A name the server never made a job under. Withdrawing it lets the picker
    // move again and puts the balance check back in front of the next attempt.
    private void WithdrawRequestName(AiRequestName name)
    {
        if (_requestKey.WithdrawAfterNoReservation(name))
            ForgetSettled(name);
    }

    // Forgets a settled name, and the recovered one when that settles the recovery as well.
    private void ForgetSettled(AiRequestName name)
    {
        Forget(name);
        _recovery.Settle(name, recovered =>
        {
            if (!string.Equals(recovered.Key, name.Key, StringComparison.Ordinal))
                Forget(new AiRequestName(recovered.Key, IsRepeat: true));
        });
    }

    private void Forget(AiRequestName name)
    {
        _outstanding.Forget(name);
        _outstandingRevision.Value++;
    }

    // Whether any request still waiting to be collected belongs to this task.
    // Each of the five is its own operation with its own models and its own
    // price, so a name outstanding on one says nothing about another.
    private bool HoldsNameFor(string task)
    {
        AiOperationId operation = AiOperations.ImageEdit(new AiImageEditTaskId(task));
        return _outstanding.Any(request => IsFor(request, task))
            || _requestKey.HasPersistedFor(operation);
    }

    private AiModelId? ModelOfOutstandingRequestFor(string task)
        => _outstanding.TryFind(request => IsFor(request, task), out string?[] held)
            && held[ModelPartIndex] is { } model
                ? new AiModelId(model)
                : _requestKey.PreferredPersistedModel(
                    AiOperations.ImageEdit(new AiImageEditTaskId(task)));

    private IReadOnlyList<AiModelId> ModelsOfOutstandingRequestsFor(AiOperationId operation)
        => _outstanding.All()
            .Where(request => request[TaskPartIndex] is { } task
                && AiOperations.ImageEdit(new AiImageEditTaskId(task)) == operation)
            .Select(request => request[ModelPartIndex])
            .OfType<string>()
            .Concat(_requestKey.PersistedModels(operation).Select(model => model.Value))
            .Distinct(StringComparer.Ordinal)
            .Select(model => new AiModelId(model))
            .ToArray();

    private static bool IsFor(string?[] request, string task)
        => string.Equals(request[TaskPartIndex], task, StringComparison.Ordinal);
}
