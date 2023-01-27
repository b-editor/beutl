using Avalonia;

using Beutl.Controls.PropertyEditors;
using Beutl.Framework;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.ViewModels.Editors;

public sealed class RelativePointEditorViewModel : BaseEditorViewModel<Graphics.RelativePoint>
{
    public RelativePointEditorViewModel(IAbstractProperty<Graphics.RelativePoint> property)
        : base(property)
    {
        FirstValue = property.GetObservable()
            .Select(x => x.Point.X)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);

        SecondValue = property.GetObservable()
            .Select(x => x.Point.Y)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);
    }

    public ReadOnlyReactivePropertySlim<float> FirstValue { get; }

    public ReadOnlyReactivePropertySlim<float> SecondValue { get; }

    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        if (visitor is RelativePointEditor editor)
        {
            editor[!RelativePointEditor.FirstValueProperty] = FirstValue.ToBinding();
            editor[!RelativePointEditor.SecondValueProperty] = SecondValue.ToBinding();
            editor.ValueChanged += OnValueChanged;
            editor.ValueChanging += OnValueChanging;
        }
    }

    private void OnValueChanged(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (e is PropertyEditorValueChangedEventArgs<Graphics.RelativePoint> args)
        {
            SetValue(args.OldValue, args.NewValue);
        }
    }

    private void OnValueChanging(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (e is PropertyEditorValueChangedEventArgs<Graphics.RelativePoint> args
            && sender is RelativePointEditor editor)
        {
            WrappedProperty.SetValue(args.NewValue);
            Graphics.RelativePoint coerced = WrappedProperty.GetValue();
            editor.FirstValue = coerced.Point.X;
            editor.SecondValue = coerced.Point.Y;
            editor.Unit = coerced.Unit;
        }
    }
}
