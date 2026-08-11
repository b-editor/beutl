using System.Reactive.Disposables;
using Beutl.Language;
using Reactive.Bindings;

namespace Beutl.ViewModels;

internal sealed class AiUsageEstimateViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = [];

    // Availability is decided by the server. The client only reflects it, so no
    // pricing information is needed here.
    public AiUsageEstimateViewModel(AiUsageViewModel usage, IObservable<bool> isAvailable)
    {
        ArgumentNullException.ThrowIfNull(usage);
        ArgumentNullException.ThrowIfNull(isAvailable);

        IsAvailable = usage.HasSnapshot
            .CombineLatest(isAvailable, (hasSnapshot, available) => hasSnapshot && available)
            .ToReadOnlyReactivePropertySlim(false)
            .DisposeWith(_disposables);

        CanAfford = usage.CanUseAi
            .CombineLatest(IsAvailable, (canUseAi, available) => canUseAi && available)
            .ToReadOnlyReactivePropertySlim(false)
            .DisposeWith(_disposables);

        IsInsufficient = usage.HasSnapshot
            .CombineLatest(
                usage.CanUseAi,
                IsAvailable,
                (hasSnapshot, canUseAi, available) => hasSnapshot && canUseAi && !available)
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

        Explanation = usage.IsMonthlyAllowanceExhausted
            .CombineLatest(
                usage.HasAdditionalCredits,
                IsInsufficient,
                CreateExplanation)
            .ToReadOnlyReactivePropertySlim(string.Empty)
            .DisposeWith(_disposables);

        HasExplanation = Explanation
            .Select(explanation => !string.IsNullOrEmpty(explanation))
            .ToReadOnlyReactivePropertySlim(false)
            .DisposeWith(_disposables);
    }

    public ReadOnlyReactivePropertySlim<bool> IsAvailable { get; }

    public ReadOnlyReactivePropertySlim<bool> CanAfford { get; }

    public ReadOnlyReactivePropertySlim<bool> IsInsufficient { get; }

    public ReadOnlyReactivePropertySlim<string> Summary { get; }

    public ReadOnlyReactivePropertySlim<bool> HasSummary { get; }

    public ReadOnlyReactivePropertySlim<string> Explanation { get; }

    public ReadOnlyReactivePropertySlim<bool> HasExplanation { get; }

    public void Dispose() => _disposables.Dispose();

    private static string CreateExplanation(
        bool isExhausted,
        bool hasAdditionalCredits,
        bool isInsufficient)
    {
        if (isInsufficient)
            return Strings.AiEstimatedUsageInsufficient;
        if (!isExhausted)
            return Strings.AiEstimatedUsageMonthly;
        return hasAdditionalCredits ? Strings.AiEstimatedUsageTopUp : string.Empty;
    }
}
