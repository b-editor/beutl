using System.Reactive.Disposables;
using Beutl.Api.Services;
using Beutl.Language;
using Reactive.Bindings;

namespace Beutl.ViewModels;

internal sealed class AiUsageEstimateViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = [];

    // Availability is decided by the server. The client only reflects it, so no
    // pricing information is needed here. Until the server has answered the
    // state is Unknown, which is neither a go-ahead nor a shortfall: the run
    // stays offered and nothing is claimed about the balance, because the
    // authoritative check runs again before the paid request is sent.
    public AiUsageEstimateViewModel(
        AiUsageViewModel usage,
        IObservable<AiOperationAvailabilityState> availability)
    {
        ArgumentNullException.ThrowIfNull(usage);
        ArgumentNullException.ThrowIfNull(availability);

        State = usage.HasSnapshot
            .CombineLatest(
                availability,
                (hasSnapshot, state) => hasSnapshot ? state : AiOperationAvailabilityState.Unknown)
            .ToReadOnlyReactivePropertySlim(AiOperationAvailabilityState.Unknown)
            .DisposeWith(_disposables);

        CanAfford = usage.CanUseAi
            .CombineLatest(
                State,
                (canUseAi, state) => canUseAi && state != AiOperationAvailabilityState.Unavailable)
            .ToReadOnlyReactivePropertySlim(false)
            .DisposeWith(_disposables);

        IsInsufficient = usage.CanUseAi
            .CombineLatest(
                State,
                (canUseAi, state) => canUseAi && state == AiOperationAvailabilityState.Unavailable)
            .ToReadOnlyReactivePropertySlim(false)
            .DisposeWith(_disposables);

        Summary = usage.HasSnapshot
            .CombineLatest(
                usage.CanUseAi,
                (hasSnapshot, canUseAi) =>
                    hasSnapshot && !canUseAi ? Strings.AiPricingUnavailable : string.Empty)
            .ToReadOnlyReactivePropertySlim(string.Empty)
            .DisposeWith(_disposables);

        HasSummary = Summary
            .Select(summary => !string.IsNullOrEmpty(summary))
            .ToReadOnlyReactivePropertySlim(true)
            .DisposeWith(_disposables);

        Explanation = State
            .CombineLatest(
                usage.IsMonthlyAllowanceExhausted,
                usage.HasAdditionalCredits,
                CreateExplanation)
            .ToReadOnlyReactivePropertySlim(string.Empty)
            .DisposeWith(_disposables);

        HasExplanation = Explanation
            .Select(explanation => !string.IsNullOrEmpty(explanation))
            .ToReadOnlyReactivePropertySlim(false)
            .DisposeWith(_disposables);
    }

    public ReadOnlyReactivePropertySlim<AiOperationAvailabilityState> State { get; }

    /// <summary>
    /// Whether the run may be offered. An unanswered check leaves this true so a
    /// pending or failed lookup does not lock the account out of what it paid for.
    /// </summary>
    public ReadOnlyReactivePropertySlim<bool> CanAfford { get; }

    /// <summary>True only where the server actually refused the operation.</summary>
    public ReadOnlyReactivePropertySlim<bool> IsInsufficient { get; }

    public ReadOnlyReactivePropertySlim<string> Summary { get; }

    public ReadOnlyReactivePropertySlim<bool> HasSummary { get; }

    public ReadOnlyReactivePropertySlim<string> Explanation { get; }

    public ReadOnlyReactivePropertySlim<bool> HasExplanation { get; }

    public void Dispose() => _disposables.Dispose();

    private static string CreateExplanation(
        AiOperationAvailabilityState state,
        bool isExhausted,
        bool hasAdditionalCredits)
    {
        return state switch
        {
            AiOperationAvailabilityState.Unavailable => Strings.AiEstimatedUsageInsufficient,
            AiOperationAvailabilityState.Available when !isExhausted => Strings.AiEstimatedUsageMonthly,
            AiOperationAvailabilityState.Available when hasAdditionalCredits => Strings.AiEstimatedUsageTopUp,
            _ => string.Empty,
        };
    }
}
