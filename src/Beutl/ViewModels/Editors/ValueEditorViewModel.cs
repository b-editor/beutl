using Beutl.Animation;
using Beutl.Graphics.Rendering;
using Beutl.Operation;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.ViewModels.Editors;

public class ValueEditorViewModel<T> : BaseEditorViewModel<T>
{
    public ValueEditorViewModel(IPropertyAdapter<T> property)
        : base(property)
    {
        // Expressionが設定されている場合は、PropertyAdapterの値を直接表示（実際の評価値）
        // Expressionが設定されていない場合は、EditingKeyFrameまたはPropertyAdapterの値を表示
        Value = HasExpression
            .Select(hasExpression =>
            {
                if (hasExpression && PropertyAdapter is EnginePropertyAdapter<T> { Property: var engineProperty })
                {
                    return CurrentTime.Select(t => engineProperty.GetValue(new RenderContext(t)));
                }
                else
                {
                    // Expressionが設定されていない場合は通常の動作
                    return EditingKeyFrame
                        .Select(x => x?.GetObservable(KeyFrame<T>.ValueProperty))
                        .Select(x => x ?? PropertyAdapter.GetObservable())
                        .Switch();
                }
            })
            .Switch()
            .ToReadOnlyReactiveProperty()
            .AddTo(Disposables)!;
    }

    public ReadOnlyReactiveProperty<T> Value { get; }
}
