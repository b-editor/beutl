using Beutl.Graphics.Effects;
using Reactive.Bindings;

namespace Beutl.Editor.Components.PropertyEditors;

internal interface IEffectComparisonSelection
{
    IReadOnlyReactiveProperty<bool> IsSelectingEffectForComparison { get; }

    bool CanCompareEffect(FilterEffect effect);

    bool TryChooseComparisonEffect(FilterEffect effect);
}
