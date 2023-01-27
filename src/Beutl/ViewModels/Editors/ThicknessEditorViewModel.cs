using Avalonia;

using Beutl.Controls.PropertyEditors;
using Beutl.Framework;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.ViewModels.Editors;

public sealed class ThicknessEditorViewModel : BaseEditorViewModel<Graphics.Thickness>
{
    public ThicknessEditorViewModel(IAbstractProperty<Graphics.Thickness> property)
        : base(property)
    {
        FirstValue = property.GetObservable()
            .Select(x => x.Left)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);

        SecondValue = property.GetObservable()
            .Select(x => x.Top)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);

        ThirdValue = property.GetObservable()
            .Select(x => x.Right)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);

        FourthValue = property.GetObservable()
            .Select(x => x.Bottom)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);
    }

    public ReadOnlyReactivePropertySlim<float> FirstValue { get; }

    public ReadOnlyReactivePropertySlim<float> SecondValue { get; }

    public ReadOnlyReactivePropertySlim<float> ThirdValue { get; }

    public ReadOnlyReactivePropertySlim<float> FourthValue { get; }

    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        if (visitor is Vector4Editor<float> editor)
        {
            editor[!Vector4Editor<float>.FirstValueProperty] = FirstValue.ToBinding();
            editor[!Vector4Editor<float>.SecondValueProperty] = SecondValue.ToBinding();
            editor[!Vector4Editor<float>.ThirdValueProperty] = ThirdValue.ToBinding();
            editor[!Vector4Editor<float>.FourthValueProperty] = FourthValue.ToBinding();
            editor.ValueChanged += OnValueChanged;
            editor.ValueChanging += OnValueChanging;
        }
    }

    private void OnValueChanged(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (e is PropertyEditorValueChangedEventArgs<(float Left, float Top, float Right, float Bottom)> args)
        {
            SetValue(new Graphics.Thickness(args.OldValue.Left, args.OldValue.Top, args.OldValue.Right, args.OldValue.Bottom),
                     new Graphics.Thickness(args.NewValue.Left, args.NewValue.Top, args.NewValue.Right, args.NewValue.Bottom));
        }
    }

    private void OnValueChanging(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (sender is Vector4Editor<float> editor)
        {
            WrappedProperty.SetValue(new Graphics.Thickness(editor.FirstValue, editor.SecondValue, editor.ThirdValue, editor.FourthValue));
            Graphics.Thickness coerced = WrappedProperty.GetValue();
            editor.FirstValue = coerced.Left;
            editor.SecondValue = coerced.Top;
            editor.ThirdValue = coerced.Right;
            editor.FourthValue = coerced.Bottom;
        }
    }
}
