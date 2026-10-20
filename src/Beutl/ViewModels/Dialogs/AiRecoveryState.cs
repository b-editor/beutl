using Beutl.Api.Services;
using Beutl.Services;
using Beutl.Services.AI;
using Reactive.Bindings;

namespace Beutl.ViewModels.Dialogs;

/// <summary>
/// What the image generation, image edit and video generation dialogs keep alike for recovery: the
/// attempts listed and the commands over them.
/// </summary>
internal sealed class AiRecoveryState : IDisposable
{
    private readonly CompositeDisposable _disposables = [];
    private readonly AiRequestKey _requestKey;
    private readonly AiOperationId _operation;
    private readonly ReactivePropertySlim<string?> _error;
    private readonly AiRecoveryLogs _logs;
    private readonly Func<AiPendingAttempt, Task> _collect;
    private readonly Action<AiPendingAttempt>? _settled;

    // collect looks an attempt's job up by its key; settled hears of every attempt let go of.
    public AiRecoveryState(
        AiRequestKey requestKey,
        AiOperationId operation,
        ReactivePropertySlim<string?> error,
        AiRecoveryLogs logs,
        Func<AiPendingAttempt, Task> collect,
        Action<AiPendingAttempt>? settled = null)
    {
        _requestKey = requestKey;
        _operation = operation;
        _error = error;
        _logs = logs;
        _collect = collect;
        _settled = settled;

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
                _ = _collect(attempt);
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

    // A single attempt is collected as the dialog opens; several wait for the person to choose.
    public void TryAutoRecoverSingleAttempt()
    {
        IReadOnlyList<AiPendingAttempt> attempts = PendingAttempts();
        if (attempts.Count == 1)
            _ = _collect(attempts[0]);

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
        if (!TrySettle(attempt))
            return;
        // A request that was sent before may still finish, or may already have been paid for.
        // Either way its job stays in the account's AI job list.
        NotificationService.ShowInformation(Strings.AiPendingRequest, Strings.AiAbandonedRequestInJobs);
    }

    // Drops an attempt whose job is settled, deleted or was never made.
    public bool TrySettle(AiPendingAttempt attempt)
    {
        try
        {
            _requestKey.Settle(attempt);
            _settled?.Invoke(attempt);
            return true;
        }
        catch (Exception ex) when (ex is InvalidDataException or AuthenticationRequiredException)
        {
            _logs.AbandonFailed(ex);
            _error.Value = Strings.AiResultUnavailable;
            return false;
        }
        finally
        {
            Revision.Value++;
        }
    }

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
