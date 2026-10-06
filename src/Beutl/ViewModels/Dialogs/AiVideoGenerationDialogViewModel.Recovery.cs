using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Reactive.Disposables;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Beutl.Api;
using Beutl.Api.Services;
using Beutl.Editor.Services;
using Beutl.Graphics;
using Beutl.Language;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Services.AI;
using Beutl.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.ViewModels.Dialogs;

internal sealed partial class AiVideoGenerationDialogViewModel
{
    private IReadOnlyList<AiPendingAttempt> GetPendingRecoveryAttempts()
    {
        try
        {
            return _requestKey.PendingAttempts(Operation);
        }
        catch (InvalidDataException ex)
        {
            _logger.LogError(ex, "Failed to read video-generation recovery attempts.");
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
            ex => _logger.LogError(ex, "Failed to clear video-generation state after an account change."));

    private void TryAutoRecoverForCurrentIdentity()
    {
        if (_requestKey.CurrentAccountId is not null)
            TryAutoRecoverSingleAttempt();
    }

    private void ClearIdentityState()
    {
        _runningRequest = null;
        IsGenerating.Value = false;
        IsWaitingForJob.Value = false;
        ClearActiveRecovery();
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
        _resultSnapshot = null;
        StatusText.Value = InitialStatusText;
        Error.Value = null;
        ModelPicker.ReconcileRecoveryModels();
        ApplyModelCapabilities(ModelPicker.Selected.Value?.Model);
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
        // Old job-based attempts remain listed for explicit abandonment; they
        // cannot be converted into a new uploaded-source request.
        if (attempt.Form?.SourceJobId is not null)
        {
            Error.Value = Strings.AiResultUnavailable;
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
            foreach (AiRequestRecoverySource source in attempt.EffectiveSources)
                _ = _requestKey.ReadSourceBytes(source);
        }
        catch (InvalidDataException ex)
        {
            // Keep the row and key until the user explicitly abandons it.
            _logger.LogWarning(ex, "Video-generation recovery source is unavailable.");
            Error.Value = Strings.AiResultUnavailable;
            return false;
        }

        AiRequestFormSnapshot form = attempt.Form!;
        if (paths.Count != attempt.EffectiveSources.Count)
        {
            Error.Value = Strings.AiResultUnavailable;
            return false;
        }

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
        ActivateRecovery(attempt);
        RestoreVideoInputs(attempt, paths);
        ApplyModelCapabilities(ModelPicker.Selected.Value?.Model);
        _recoveryRevision.Value++;
        SelectRecoveredModel();
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
                ApplyModelCapabilities(ModelPicker.Selected.Value?.Model);
            }

            _recoveryRevision.Value++;
        }
        catch (Exception ex) when (ex is InvalidDataException or AuthenticationRequiredException)
        {
            _logger.LogWarning(ex, "Failed to abandon video-generation recovery attempt.");
            Error.Value = Strings.AiResultUnavailable;
        }
    }

    // The model a request should carry: the one the outstanding name was built
    // from while there is one, and the picker's otherwise.
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
        ReleaseFramesOf(name);
        if (_selectedRecovery is { } selected
            && AiFormRecovery.IsSettledBy(_requestKey, selected, name))
        {
            ReleaseFramesOf(new AiRequestName(selected.Key, IsRepeat: true));
            ClearActiveRecovery();
            ModelPicker.ReconcileRecoveryModels();
            ApplyModelCapabilities(ModelPicker.Selected.Value?.Model);
            _recoveryRevision.Value++;
        }
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
        if (_selectedRecovery is { } selected
            && AiFormRecovery.IsSettledBy(_requestKey, selected, name))
        {
            ReleaseFramesOf(new AiRequestName(selected.Key, IsRepeat: true));
            ClearActiveRecovery();
            ModelPicker.ReconcileRecoveryModels();
            ApplyModelCapabilities(ModelPicker.Selected.Value?.Model);
            _recoveryRevision.Value++;
        }
    }
}
