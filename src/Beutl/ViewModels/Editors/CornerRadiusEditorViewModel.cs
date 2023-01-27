using Avalonia;

using Beutl.Controls.PropertyEditors;
using Beutl.Framework;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.ViewModels.Editors;

public sealed class CornerRadiusEditorViewModel : BaseEditorViewModel<Media.CornerRadius>
{
    public CornerRadiusEditorViewModel(IAbstractProperty<Media.CornerRadius> property)
        : base(property)
    {
        FirstValue = property.GetObservable()
            .Select(x => x.TopLeft)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);

        SecondValue = property.GetObservable()
            .Select(x => x.TopRight)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);

        ThirdValue = property.GetObservable()
            .Select(x => x.BottomRight)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);

        FourthValue = property.GetObservable()
            .Select(x => x.BottomLeft)
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
        if (e is PropertyEditorValueChangedEventArgs<(float TopLeft, float TopRight, float BottomRight, float BottomLeft)> args)
        {
            SetValue(new Media.CornerRadius(args.OldValue.TopLeft, args.OldValue.TopRight, args.OldValue.BottomRight, args.OldValue.BottomLeft),
                     new Media.CornerRadius(args.NewValue.TopLeft, args.NewValue.TopRight, args.NewValue.BottomRight, args.NewValue.BottomLeft));
        }
    }

    private void OnValueChanging(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (sender is Vector4Editor<float> editor)
        {
            WrappedProperty.SetValue(new Media.CornerRadius(editor.FirstValue, editor.SecondValue, editor.ThirdValue, editor.FourthValue));
            Media.CornerRadius coerced = WrappedProperty.GetValue();
            editor.FirstValue = coerced.TopLeft;
            editor.SecondValue = coerced.TopRight;
            editor.ThirdValue = coerced.BottomRight;
            editor.FourthValue = coerced.BottomLeft;
        }
    }
}
