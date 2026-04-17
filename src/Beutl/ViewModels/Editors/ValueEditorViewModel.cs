using Avalonia;
using Avalonia.Interactivity;
using Beutl.Animation;
using Beutl.Composition;
using Beutl.Controls.PropertyEditors;
using Beutl.PropertyAdapters;
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
                hasExpression && PropertyAdapter is EnginePropertyAdapter<T> { Property: var engineProperty }
                    ? CurrentTime.Select(t => engineProperty.GetValue(new CompositionContext(t)))
                    // Expressionが設定されていない場合は通常の動作
                    : EditingKeyFrame
                        .Select(x => x?.GetObservable(KeyFrame<T>.ValueProperty))
                        .Select(x => x ?? PropertyAdapter.GetObservable())
                        .Switch())
            .Switch()
            .ToReadOnlyReactiveProperty()
            .AddTo(Disposables)!;
    }

    public ReadOnlyReactiveProperty<T> Value { get; }

    protected void AttachValueBindings<TEditor>(
        TEditor editor,
        AvaloniaProperty valueProperty,
        Func<TEditor, T> getValue,
        Action<TEditor, T> setValue)
        where TEditor : PropertyEditor
    {
        editor.Bind(valueProperty, Value.ToBinding())
            .DisposeWith(Disposables);

        editor.AddDisposableHandler(
                PropertyEditor.ValueChangedEvent,
                (s, _) =>
                {
                    if (s is TEditor ed)
                    {
                        setValue(ed, SetCurrentValueAndGetCoerced(getValue(ed))!);
                    }
                })
            .DisposeWith(Disposables);

        editor.AddDisposableHandler(
                PropertyEditor.ValueConfirmedEvent,
                (_, e) =>
                {
                    if (e is PropertyEditorValueChangedEventArgs<T> args)
                    {
                        SetValue(args.OldValue, args.NewValue);
                    }
                })
            .DisposeWith(Disposables);
    }
}
