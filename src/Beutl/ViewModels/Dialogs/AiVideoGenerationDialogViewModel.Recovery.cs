using Beutl.Api.Services;
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
        _recovery.Clear();
        ClearVideoInputs();
        _chosenDuration = null;
        _chosenResolution = null;
        _chosenAspectRatio = null;
        _chosenAudio = true;
        _chosenSeed = null;
        SetFrameCore(isFirstFrame: true, null, null);
        SetFrameCore(isFirstFrame: false, null, null);
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

    internal bool TryRecoverPendingAttempt(AiPendingAttempt attempt)
    {
        if (_requestKey.CurrentAccountId is not { } account
            || !StringComparer.Ordinal.Equals(account, attempt.AccountId))
        {
            Error.Value = Strings.AiAuthenticationRequired;
            return false;
        }
        if (!attempt.HasCanonicalForm
            || !string.Equals(attempt.Operation, Operation.Value, StringComparison.Ordinal))
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
            // Keep the row and key until the user explicitly abandons it.
            _logger.LogWarning(ex, "Video-generation recovery source is unavailable.");
            Error.Value = Strings.AiResultUnavailable;
            return false;
        }

        AiRequestFormSnapshot form = attempt.Form!;
        if (attempt.EffectiveSources.Any(source =>
            source.Role.StartsWith(ReferenceRolePrefix, StringComparison.Ordinal)
            && !ReferenceGroups.Any(group => source.Role.StartsWith($"{ReferenceRolePrefix}{group.Kind}-", StringComparison.Ordinal))))
        {
            Error.Value = Strings.AiResultUnavailable;
            return false;
        }

        _applyingCapabilities = true;
        try
        {
            Prompt.Value = form.Prompt ?? string.Empty;
            Style.Value = IsGeneration ? form.Style ?? string.Empty : string.Empty;
            Composition.Value = IsGeneration ? form.Composition ?? string.Empty : string.Empty;
            Motion.Value = IsGeneration ? form.Motion ?? string.Empty : string.Empty;
            Exclusions.Value = IsGeneration ? form.Exclusions ?? string.Empty : string.Empty;
            _chosenDuration = form.DurationSeconds is { } seconds
                ? new AiVideoDurationOption(seconds)
                : SelectedDuration.Value;
            _chosenResolution = form.Resolution is { } resolution
                ? new AiVideoResolutionOption(resolution)
                : SelectedResolution.Value;
            _chosenAspectRatio = form.AspectRatio is { } aspect
                ? new AiVideoAspectRatioOption(aspect)
                : SelectedAspectRatio.Value;
            _chosenAudio = form.GenerateAudio ?? true;
            _chosenSeed = form.Seed;
            ApplyRecoveredScalarSelections(form);
        }
        finally
        {
            _applyingCapabilities = false;
        }

        string? firstPath = null;
        string? lastPath = null;
        string? firstElement = form.FirstFrameElementId;
        string? lastElement = form.LastFrameElementId;
        for (int index = 0; index < attempt.EffectiveSources.Count; index++)
        {
            AiRequestRecoverySource source = attempt.EffectiveSources[index];
            string path = paths[index];
            if (source.Role == FirstFrameRole)
            {
                firstPath = path;
                firstElement ??= source.ElementId;
            }
            else if (source.Role == LastFrameRole)
            {
                lastPath = path;
                lastElement ??= source.ElementId;
            }
        }

        SetFrameCore(isFirstFrame: true, firstPath, firstElement);
        SetFrameCore(isFirstFrame: false, lastPath, lastElement);
        _recovery.Activate(attempt);
        RestoreVideoInputs(attempt, paths);
        ApplyModelCapabilities(ModelPicker.Selected.Value?.Model);
        _recovery.Revision.Value++;
        _recovery.SelectRecoveredModel();
        return true;
    }

    private void ApplyRecoveredScalarSelections(AiRequestFormSnapshot form)
    {
        if (form.DurationSeconds is { } duration
            && DurationOptions.FirstOrDefault(option => option.Seconds == duration) is { } durationOption)
            SelectedDuration.Value = durationOption;
        if (form.Resolution is { } resolution
            && ResolutionOptions.FirstOrDefault(option => option.Value == resolution) is { } resolutionOption)
            SelectedResolution.Value = resolutionOption;
        if (form.AspectRatio is { } aspect
            && AspectRatioOptions.FirstOrDefault(option => option.Value == aspect) is { } aspectOption)
            SelectedAspectRatio.Value = aspectOption;
        GenerateAudio.Value = form.GenerateAudio ?? true;
        Seed.Value = form.Seed;
    }

    internal void AbandonPendingAttempt(AiPendingAttempt attempt) => _recovery.Abandon(attempt);

    // Only this one request is settled. Retiring the whole run instead would
    // throw away the name of anything else still waiting to be collected.
    private void RetireRequestName(AiRequestName name)
    {
        if (!_requestKey.Retire(name))
            return;
        ReleaseFramesOfSettled(name);
        // Reloads were held back while that name was outstanding, so this is
        // where an operator's change to the model list finally lands.
        _ = RefreshModelsAsync();
    }

    // A name the server never made a job under. Withdrawing it lets the picker
    // move again and puts the balance check back in front of the next attempt.
    private void WithdrawRequestName(AiRequestName name)
    {
        if (_requestKey.WithdrawAfterNoReservation(name))
            ReleaseFramesOfSettled(name);
    }

    // Releases the frames of a settled name, and of the recovered one when that settles the
    // recovery as well.
    private void ReleaseFramesOfSettled(AiRequestName name)
    {
        ReleaseFramesOf(name);
        _recovery.Settle(name, recovered => ReleaseFramesOf(new AiRequestName(recovered.Key, IsRepeat: true)));
    }
}
