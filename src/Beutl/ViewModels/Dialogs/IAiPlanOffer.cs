using Reactive.Bindings;

namespace Beutl.ViewModels.Dialogs;

/// <summary>
/// A screen that offers the Pro plan when the account cannot use AI.
/// </summary>
internal interface IAiPlanOffer
{
    ReadOnlyReactivePropertySlim<bool> ShowJoinPro { get; }

    ReactiveCommand OpenAiPlan { get; }
}
