using System.Reactive.Disposables;
using Beutl.Api.Services;
using Beutl.Language;
using Reactive.Bindings;

namespace Beutl.ViewModels;

internal sealed class AiUsageViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = [];

    public AiUsageViewModel(IObservable<AiEntitlements?> entitlements)
    {
        CanUseAi = entitlements
            .Select(entitlements => entitlements?.CanUseAi == true)
            .ToReadOnlyReactivePropertySlim(false)
            .DisposeWith(_disposables);

        HasSnapshot = entitlements
            .Select(entitlements => entitlements != null)
            .ToReadOnlyReactivePropertySlim(false)
            .DisposeWith(_disposables);

        // The server sends monthly usage as a proportion rather than raw units.
        UsagePercent = entitlements
            .Select(entitlements => entitlements?.Balance.MonthlyUsage.UsedPercent ?? 0)
            .ToReadOnlyReactivePropertySlim(0)
            .DisposeWith(_disposables);

        RemainingPercent = entitlements
            .Select(entitlements => entitlements?.Balance.MonthlyUsage.RemainingPercent ?? 100)
            .ToReadOnlyReactivePropertySlim(100)
            .DisposeWith(_disposables);

        IsMonthlyAllowanceExhausted = entitlements
            .Select(entitlements => entitlements?.Balance.MonthlyUsage.IsExhausted ?? false)
            .ToReadOnlyReactivePropertySlim(false)
            .DisposeWith(_disposables);

        HasAdditionalCreditDebt = entitlements
            .Select(entitlements => entitlements?.Balance.HasAdditionalCreditDebt ?? false)
            .ToReadOnlyReactivePropertySlim(false)
            .DisposeWith(_disposables);

        MonthlyUsageText = UsagePercent
            .Select(percent => string.Format(Strings.AiMonthlyUsage, percent))
            .ToReadOnlyReactivePropertySlim(string.Format(Strings.AiMonthlyUsage, 0))
            .DisposeWith(_disposables);

        MonthlyRemainingText = RemainingPercent
            .Select(percent => string.Format(Strings.AiMonthlyUsageRemaining, percent))
            .ToReadOnlyReactivePropertySlim(string.Format(Strings.AiMonthlyUsageRemaining, 100))
            .DisposeWith(_disposables);

        HasAdditionalCredits = entitlements
            .Select(entitlements => (entitlements?.Balance.AdditionalCredits ?? 0) > 0)
            .ToReadOnlyReactivePropertySlim(false)
            .DisposeWith(_disposables);

        AdditionalCredits = entitlements
            .Select(entitlements => entitlements?.Balance.AdditionalCredits ?? 0)
            .ToReadOnlyReactivePropertySlim(0)
            .DisposeWith(_disposables);

        AdditionalCreditsText = AdditionalCredits
            .Select(credits => string.Format(Strings.AiAdditionalCredits, credits))
            .ToReadOnlyReactivePropertySlim(string.Format(Strings.AiAdditionalCredits, 0))
            .DisposeWith(_disposables);

        AdditionalCreditDebtText = Observable.Return(Strings.AiAdditionalCreditDebt)
            .ToReadOnlyReactivePropertySlim(Strings.AiAdditionalCreditDebt)
            .DisposeWith(_disposables);
    }

    public ReadOnlyReactivePropertySlim<bool> CanUseAi { get; }

    public ReadOnlyReactivePropertySlim<bool> HasSnapshot { get; }

    public ReadOnlyReactivePropertySlim<bool> HasAdditionalCreditDebt { get; }

    public ReadOnlyReactivePropertySlim<bool> HasAdditionalCredits { get; }

    public ReadOnlyReactivePropertySlim<int> AdditionalCredits { get; }

    public ReadOnlyReactivePropertySlim<int> UsagePercent { get; }

    public ReadOnlyReactivePropertySlim<int> RemainingPercent { get; }

    public ReadOnlyReactivePropertySlim<bool> IsMonthlyAllowanceExhausted { get; }

    public ReadOnlyReactivePropertySlim<string> MonthlyUsageText { get; }

    public ReadOnlyReactivePropertySlim<string> MonthlyRemainingText { get; }

    public ReadOnlyReactivePropertySlim<string> AdditionalCreditsText { get; }

    public ReadOnlyReactivePropertySlim<string> AdditionalCreditDebtText { get; }

    public void Dispose()
    {
        _disposables.Dispose();
    }
}
