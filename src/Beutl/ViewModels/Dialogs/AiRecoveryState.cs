using Beutl.Api.Services;
using Beutl.Services;
using Beutl.Services.AI;
using Reactive.Bindings;

namespace Beutl.ViewModels.Dialogs;

/// <summary>
/// What the image generation, image edit and video generation dialogs keep alike for recovery: the
/// attempts listed, the one on the form, and the commands over them.
/// </summary>
internal sealed class AiRecoveryState : IDisposable
{
    private readonly CompositeDisposable _disposables = [];
    private readonly AiRequestKey _requestKey;
    private readonly AiModelPickerViewModel _modelPicker;
    private readonly AiOperationId _operation;
    private readonly ReactivePropertySlim<string?> _error;
    private readonly AiRecoveryLogs _logs;
    private readonly Func<AiPendingAttempt, bool> _recover;
    private readonly Action? _reapply;

    // recover puts an attempt back on the dialog's form; reapply is what the dialog applies again
    // once a recovery ends and the picker has been reconciled.
    public AiRecoveryState(
        AiRequestKey requestKey,
        AiModelPickerViewModel modelPicker,
        AiOperationId operation,
        ReactivePropertySlim<string?> error,
        AiRecoveryLogs logs,
        Func<AiPendingAttempt, bool> recover,
        Action? reapply = null)
    {
        _requestKey = requestKey;
        _modelPicker = modelPicker;
        _operation = operation;
        _error = error;
        _logs = logs;
        _recover = recover;
        _reapply = reapply;

        SelectedAttempt = new ReactivePropertySlim<AiPendingAttempt?>()
            .DisposeWith(_disposables);
        Attempts = Revision
            .Select(_ => PendingAttempts())
            .ToReadOnlyReactivePropertySlim(Array.Empty<AiPendingAttempt>())
            .DisposeWith(_disposables);
        Available = Attempts
            .Select(attempts => attempts.Count != 0)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        RecoverSelected = new ReactiveCommand();
        RecoverSelected.Subscribe(() =>
        {
            if (SelectedAttempt.Value is { } attempt)
                _recover(attempt);
        }).DisposeWith(_disposables);
        AbandonSelected = new ReactiveCommand();
        AbandonSelected.Subscribe(() =>
        {
            if (SelectedAttempt.Value is { } attempt)
                Abandon(attempt);
        }).DisposeWith(_disposables);
    }

    /// <summary>Raised whenever the attempts may have changed, so the list is read again.</summary>
    public ReactivePropertySlim<int> Revision { get; } = new();

    /// <summary>The attempt on the form, until it is settled, abandoned or cleared.</summary>
    public AiPendingAttempt? Active { get; private set; }

    public ReactivePropertySlim<AiPendingAttempt?> SelectedAttempt { get; }

    public ReadOnlyReactivePropertySlim<IReadOnlyList<AiPendingAttempt>> Attempts { get; }

    public ReadOnlyReactivePropertySlim<bool> Available { get; }

    public ReactiveCommand RecoverSelected { get; }

    public ReactiveCommand AbandonSelected { get; }

    private IReadOnlyList<AiPendingAttempt> PendingAttempts()
    {
        try
        {
            return _requestKey.PendingAttempts(_operation);
        }
        catch (InvalidDataException ex)
        {
            _logs.ReadFailed(ex);
            return Array.Empty<AiPendingAttempt>();
        }
    }

    public void TryAutoRecoverSingleAttempt()
    {
        IReadOnlyList<AiPendingAttempt> attempts = PendingAttempts();
        if (attempts.Count == 1 && attempts[0].HasCanonicalForm)
        {
            if (!_recover(attempts[0]))
                Clear();
        }

        Revision.Value++;
    }

    // On an account change: clear puts the form back, then the new account's attempt is recovered.
    public void SwitchIdentity(IdentityOperationLifetime identity, Action clear)
        => AiFormRecovery.SwitchIdentity(
            identity,
            clear,
            TryAutoRecoverForCurrentIdentity,
            _logs.ClearFailed);

    private void TryAutoRecoverForCurrentIdentity()
    {
        if (_requestKey.CurrentAccountId is not null)
            TryAutoRecoverSingleAttempt();
    }

    public void Abandon(AiPendingAttempt attempt)
    {
        try
        {
            _requestKey.Abandon(attempt);
            if (Active is { } active
                && AiFormRecovery.IsSameAttempt(active, attempt))
            {
                ClearAndReconcile();
            }

            Revision.Value++;
            // A request that was sent before may still finish, or may already have been paid for.
            // Either way its job stays in the account's AI job list.
            NotificationService.ShowInformation(Strings.AiPendingRequest, Strings.AiAbandonedRequestInJobs);
        }
        catch (Exception ex) when (ex is InvalidDataException or AuthenticationRequiredException)
        {
            _logs.AbandonFailed(ex);
            _error.Value = Strings.AiResultUnavailable;
        }
    }

    // Called once name is retired or withdrawn. When that settles the attempt on the form as well,
    // the attempt is handed to release before it is cleared.
    public void Settle(AiRequestName name, Action<AiPendingAttempt>? release = null)
    {
        if (Active is { } active
            && AiFormRecovery.IsSettledBy(_requestKey, active, name))
        {
            release?.Invoke(active);
            ClearAndReconcile();
            Revision.Value++;
        }
    }

    private void ClearAndReconcile()
    {
        Clear();
        _modelPicker.ReconcileRecoveryModels();
        _reapply?.Invoke();
    }

    // The recovered request's model (null when it named none); otherwise the picker's. A request
    // that named none was fingerprinted without one, so naming a model now would make it another.
    public AiModelId? ModelForRequest(AiModelId? selected)
        => Active is { } attempt
            ? attempt.Model is { } model ? new AiModelId(model) : null
            : selected;

    public void Activate(AiPendingAttempt attempt)
    {
        Active = attempt;
        SelectedAttempt.Value = attempt;
        _modelPicker.IsSelectionEnabled.Value = false;
    }

    public void Clear()
    {
        Active = null;
        SelectedAttempt.Value = null;
        _modelPicker.IsSelectionEnabled.Value = true;
    }

    public void SelectRecoveredModel()
        => AiFormRecovery.SelectRecoveredModel(_modelPicker, Active);

    public void Dispose()
    {
        Revision.Dispose();
        _disposables.Dispose();
    }
}

/// <summary>What a dialog logs, in its own words, when reading, clearing or abandoning fails.</summary>
internal sealed record AiRecoveryLogs(
    Action<Exception> ReadFailed,
    Action<Exception> ClearFailed,
    Action<Exception> AbandonFailed);
