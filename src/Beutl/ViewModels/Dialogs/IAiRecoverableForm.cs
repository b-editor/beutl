using Beutl.Services.AI;
using Reactive.Bindings;

namespace Beutl.ViewModels.Dialogs;

/// <summary>
/// A form that lists the requests still pending from an earlier run, so one can be recovered or abandoned.
/// </summary>
internal interface IAiRecoverableForm
{
    ReactivePropertySlim<AiPendingAttempt?> SelectedRecoveryAttempt { get; }

    ReadOnlyReactivePropertySlim<IReadOnlyList<AiPendingAttempt>> RecoveryAttempts { get; }

    ReadOnlyReactivePropertySlim<bool> RecoveryAvailable { get; }

    ReactiveCommand RecoverSelectedAttempt { get; }

    ReactiveCommand AbandonSelectedAttempt { get; }
}
