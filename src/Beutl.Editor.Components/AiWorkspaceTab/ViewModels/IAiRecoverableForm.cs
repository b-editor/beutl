using Beutl.Editor.Components.AiWorkspaceTab.Services;
using Reactive.Bindings;

namespace Beutl.Editor.Components.AiWorkspaceTab.ViewModels;

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
