using System.Collections.ObjectModel;
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

internal sealed partial class AiImageGenerationDialogViewModel
{
    private IReadOnlyList<AiPendingAttempt> GetPendingRecoveryAttempts()
    {
        try
        {
            return _requestKey.PendingAttempts(AiOperations.ImageGeneration);
        }
        catch (InvalidDataException ex)
        {
            _logger.LogError(ex, "Failed to read image-generation recovery attempts.");
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
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            _identityOperations.SwitchDeferred(
                action => Dispatcher.UIThread.Post(() => RunDeferredIdentityClear(action)),
                ClearIdentityState,
                TryAutoRecoverForCurrentIdentity);
            return;
        }

        _identityOperations.Switch(ClearIdentityState);
        TryAutoRecoverForCurrentIdentity();
    }

    private void RunDeferredIdentityClear(Action clear)
    {
        try
        {
            clear();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear image-generation state after an account change.");
        }
    }

    private void TryAutoRecoverForCurrentIdentity()
    {
        if (_requestKey.CurrentAccountId is not null)
            TryAutoRecoverSingleAttempt();
    }

    private void ClearIdentityState()
    {
        _runningRequest = null;
        IsGenerating.Value = false;
        ClearActiveRecovery();
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
        UpdateReferenceImageState();
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
            || !string.Equals(attempt.Operation, AiOperations.ImageGeneration.Value, StringComparison.Ordinal))
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
            // Keep the row and key. The caller must explicitly abandon it if
            // the original source can no longer be verified.
            _logger.LogWarning(ex, "Image-generation recovery source is unavailable.");
            Error.Value = Strings.AiResultUnavailable;
            return false;
        }

        AiRequestFormSnapshot form = attempt.Form!;
        string[] referencePaths = paths.ToArray();
        if (referencePaths.Length != attempt.EffectiveSources.Count)
        {
            Error.Value = Strings.AiResultUnavailable;
            return false;
        }

        _applyingCapabilities = true;
        try
        {
            Prompt.Value = form.Prompt ?? string.Empty;
            Style.Value = form.Style ?? string.Empty;
            Composition.Value = form.Composition ?? string.Empty;
            Exclusions.Value = form.Exclusions ?? string.Empty;
            if (form.AspectRatio is { } aspect)
            {
                _chosenAspectRatio = new AiImageAspectRatioOption(aspect);
                if (AspectRatioOptions.FirstOrDefault(option => option.Value == aspect) is { } aspectOption)
                    SelectedAspectRatio.Value = aspectOption;
            }
            if (form.Background is { } background)
            {
                _chosenBackground = new AiImageBackgroundOption(background);
                if (BackgroundOptions.FirstOrDefault(option => option.Value == background) is { } backgroundOption)
                    SelectedBackground.Value = backgroundOption;
            }
            Seed.Value = form.Seed;
            _chosenSeed = form.Seed;
        }
        finally
        {
            _applyingCapabilities = false;
        }

        foreach (AiReferenceImageViewModel reference in ReferenceImages)
            reference.Dispose();
        ReferenceImages.Clear();
        _chosenReferencePaths.Clear();
        foreach (string path in referencePaths)
        {
            if (LoadReference(path) is not { } reference)
            {
                Error.Value = Strings.AiResultUnavailable;
                return false;
            }

            ReferenceImages.Add(reference);
            _chosenReferencePaths.Add(path);
        }

        // Re-read the durable source bytes to ensure the path verification and
        // the preview cannot drift before the request is sent.
        foreach (AiRequestRecoverySource source in attempt.EffectiveSources)
        {
            try
            {
                _ = _requestKey.ReadSourceBytes(source);
            }
            catch (InvalidDataException ex)
            {
                _logger.LogWarning(ex, "Image-generation recovery source changed.");
                Error.Value = Strings.AiResultUnavailable;
                return false;
            }
        }

        ActivateRecovery(attempt);
        SelectRecoveredModel();
        ApplyModelCapabilities(ModelPicker.Selected.Value?.Model);
        UpdateReferenceImageState();
        _recoveryRevision.Value++;
        return true;
    }

    internal void AbandonPendingAttempt(AiPendingAttempt attempt)
    {
        try
        {
            _requestKey.Abandon(attempt);
            if (_selectedRecovery is { } selected
                && selected.AccountId == attempt.AccountId
                && selected.Operation == attempt.Operation
                && selected.Fingerprint == attempt.Fingerprint)
            {
                ClearActiveRecovery();
                ModelPicker.ReconcileRecoveryModels();
                ApplyModelCapabilities(ModelPicker.Selected.Value?.Model);
                UpdateReferenceImageState();
            }

            _recoveryRevision.Value++;
        }
        catch (Exception ex) when (ex is InvalidDataException or AuthenticationRequiredException)
        {
            _logger.LogWarning(ex, "Failed to abandon image-generation recovery attempt.");
            Error.Value = Strings.AiResultUnavailable;
        }
    }

    private AiModelId? ModelForRequest(AiModelId? selected)
        => _selectedRecovery is { } attempt
            ? attempt.Model is { } model ? new AiModelId(model) : null
            : selected;

    private bool CanUseSelectedModel(AiModelPickerOption? selected)
    {
        if (selected is null || selected.IsAvailable)
            return true;
        if (!_requestKey.HasOutstandingName.Value
            || _selectedRecovery is not { Model: { } model } recovery
            || !_requestKey.IsCurrentPending(recovery))
        {
            return false;
        }

        return selected.Id == new AiModelId(model);
    }

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
    {
        if (_selectedRecovery is not { } recovery)
            return;
        if (recovery.Model is not { } model)
        {
            ModelPicker.Selected.Value = null;
            return;
        }
        AiModelId id = new(model);
        ModelPicker.Selected.Value = ModelPicker.Options.FirstOrDefault(option => option.Id == id);
    }

    // The model a request should carry: the one the outstanding name was built
    // from while there is one, and the picker's otherwise.
    //
    // Only this one request is settled. Retiring the whole run instead would
    // throw away the name of anything else still waiting to be collected.
    private void RetireRequestName(AiRequestName name)
    {
        if (!_requestKey.Retire(name))
            return;
        if (_selectedRecovery is { } selected
            && (string.Equals(selected.Key, name.Key, StringComparison.Ordinal)
                || !_requestKey.IsCurrentPending(selected)))
        {
            ClearActiveRecovery();
            ModelPicker.ReconcileRecoveryModels();
            ApplyModelCapabilities(ModelPicker.Selected.Value?.Model);
            UpdateReferenceImageState();
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
        if (_selectedRecovery is { } selected
            && (string.Equals(selected.Key, name.Key, StringComparison.Ordinal)
                || !_requestKey.IsCurrentPending(selected)))
        {
            ClearActiveRecovery();
            ModelPicker.ReconcileRecoveryModels();
            ApplyModelCapabilities(ModelPicker.Selected.Value?.Model);
            UpdateReferenceImageState();
            _recoveryRevision.Value++;
        }
    }
}
