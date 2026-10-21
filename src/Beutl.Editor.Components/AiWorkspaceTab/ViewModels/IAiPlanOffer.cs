using Reactive.Bindings;

namespace Beutl.Editor.Components.AiWorkspaceTab.ViewModels;

/// <summary>
/// A screen that offers the Pro plan when the account cannot use AI.
/// </summary>
internal interface IAiPlanOffer
{
    ReadOnlyReactivePropertySlim<bool> ShowJoinPro { get; }

    ReactiveCommand OpenAiPlan { get; }
}
