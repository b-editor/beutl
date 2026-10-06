using Beutl.Api.Services;
using Beutl.Services.AI;
using Microsoft.Extensions.Logging;

namespace Beutl.ViewModels.Dialogs;

internal sealed partial class AiImageEditDialogViewModel
{
    private IReadOnlyList<AiPendingAttempt> GetPendingRecoveryAttempts()
    {
        try
        {
            return _requestKey.PendingAttempts(new AiOperationId("image.edit"));
        }
        catch (InvalidDataException ex)
        {
            _logger.LogError(ex, "Failed to read image-edit recovery attempts.");
            return Array.Empty<AiPendingAttempt>();
        }
    }

    private void TryAutoRecoverSingleAttempt()
    {
        IReadOnlyList<AiPendingAttempt> attempts = GetPendingRecoveryAttempts();
        if (attempts.Count == 1 && attempts[0].HasCanonicalForm)
        {
            if (!TryRecoverPendingAttempt(attempts[0]))
                ClearActiveRecovery();
        }

        _recoveryRevision.Value++;
    }

    private void OnIdentityChanged()
        => AiFormRecovery.SwitchIdentity(
            _identityOperations,
            ClearIdentityState,
            TryAutoRecoverForCurrentIdentity,
            ex => _logger.LogError(ex, "Failed to clear image-edit state after an account change."));

    private void TryAutoRecoverForCurrentIdentity()
    {
        if (_requestKey.CurrentAccountId is not null)
            TryAutoRecoverSingleAttempt();
    }

    private void ClearIdentityState()
    {
        _runningRequest = null;
        IsEditing.Value = false;
        ClearActiveRecovery();
        SourceFilePath.Value = null;
        Prompt.Value = string.Empty;
        _sourceElementId = null;
        OriginalImage.Value?.Dispose();
        OriginalImage.Value = null;
        ResultImage.Value?.Dispose();
        ResultImage.Value = null;
        Error.Value = null;
        ModelPicker.ReconcileRecoveryModels();
        _recoveryRevision.Value++;
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
            || !attempt.Operation.StartsWith("image.edit.", StringComparison.Ordinal)
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

        if (paths.Count != 1 || attempt.EffectiveSources.Count != 1)
        {
            Error.Value = Strings.AiResultUnavailable;
            return false;
        }

        try
        {
            _ = _requestKey.ReadSourceBytes(attempt.EffectiveSources[0]);
        }
        catch (InvalidDataException ex)
        {
            _logger.LogWarning(ex, "Image-edit recovery source changed.");
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
        ActivateRecovery(attempt);
        _recoveryRevision.Value++;
        SelectRecoveredModel();
        return true;
    }

    internal void AbandonPendingAttempt(AiPendingAttempt attempt)
    {
        try
        {
            _requestKey.Abandon(attempt);
            if (_selectedRecovery is { } selected
                && AiFormRecovery.IsSameAttempt(selected, attempt))
            {
                ClearActiveRecovery();
                ModelPicker.ReconcileRecoveryModels();
            }

            _recoveryRevision.Value++;
        }
        catch (Exception ex) when (ex is InvalidDataException or AuthenticationRequiredException)
        {
            _logger.LogWarning(ex, "Failed to abandon image-edit recovery attempt.");
            Error.Value = Strings.AiResultUnavailable;
        }
    }

    // The model a request should carry: the one the outstanding name was built
    // from while there is one, and the picker's otherwise.
    // Whatever the outstanding name was built from, including no model at all:
    // a request that named none was fingerprinted without one, and letting a
    // catalog that has since loaded name one would make it a different request.
    // Only for the same request: an edit of another picture, or with another
    // prompt, is a new request and is priced and run on the model on screen.
    private AiModelId? ModelForRequest(AiModelId? selected)
        => _selectedRecovery is { } attempt
            ? attempt.Model is { } model ? new AiModelId(model) : null
            : selected;

    private void ActivateRecovery(AiPendingAttempt attempt)
    {
        _selectedRecovery = attempt;
        SelectedRecoveryAttempt.Value = attempt;
        ModelPicker.IsSelectionEnabled.Value = false;
    }

    private void ClearActiveRecovery()
    {
        _selectedRecovery = null;
        SelectedRecoveryAttempt.Value = null;
        ModelPicker.IsSelectionEnabled.Value = true;
    }

    private void SelectRecoveredModel()
        => AiFormRecovery.SelectRecoveredModel(ModelPicker, _selectedRecovery);

    // Only this one request is settled. Retiring the whole run instead would
    // throw away the name of anything else still waiting to be collected.
    private void RetireRequestName(AiRequestName name)
    {
        if (!_requestKey.Retire(name))
            return;
        Forget(name);
        if (_selectedRecovery is { } selected
            && AiFormRecovery.IsSettledBy(_requestKey, selected, name))
        {
            if (!string.Equals(selected.Key, name.Key, StringComparison.Ordinal))
                Forget(new AiRequestName(selected.Key, IsRepeat: true));
            ClearActiveRecovery();
            ModelPicker.ReconcileRecoveryModels();
            _recoveryRevision.Value++;
        }
        // Reloads were held back while that name was outstanding, so this is
        // where an operator's change to the model list finally lands.
        _ = ReloadModelsAsync(SelectedTask.Value);
    }

    // A name the server never made a job under. Withdrawing it lets the picker
    // move again and puts the balance check back in front of the next attempt.
    private void WithdrawRequestName(AiRequestName name)
    {
        if (!_requestKey.WithdrawAfterNoReservation(name))
            return;
        Forget(name);
        if (_selectedRecovery is { } selected
            && AiFormRecovery.IsSettledBy(_requestKey, selected, name))
        {
            if (!string.Equals(selected.Key, name.Key, StringComparison.Ordinal))
                Forget(new AiRequestName(selected.Key, IsRepeat: true));
            ClearActiveRecovery();
            ModelPicker.ReconcileRecoveryModels();
            _recoveryRevision.Value++;
        }
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
