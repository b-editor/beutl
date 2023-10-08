using Avalonia;

using Beutl.Controls.PropertyEditors;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.ViewModels.Editors;

public sealed class CornerRadiusEditorViewModel : ValueEditorViewModel<Media.CornerRadius>, IConfigureUniformEditor
{
    public CornerRadiusEditorViewModel(IAbstractProperty<Media.CornerRadius> property)
        : base(property)
    {
        FirstValue = Value
            .Select(x => x.TopLeft)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);

        SecondValue = Value
            .Select(x => x.TopRight)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);

        ThirdValue = Value
            .Select(x => x.BottomRight)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);

        FourthValue = Value
            .Select(x => x.BottomLeft)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);
    }

    public ReadOnlyReactivePropertySlim<float> FirstValue { get; }

    public ReadOnlyReactivePropertySlim<float> SecondValue { get; }

    public ReadOnlyReactivePropertySlim<float> ThirdValue { get; }

    public ReadOnlyReactivePropertySlim<float> FourthValue { get; }

    public ReactivePropertySlim<bool> IsUniformEditorEnabled { get; } = new();

    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        if (visitor is Vector4Editor<float> editor)
        {
            editor[!Vector4Editor<float>.FirstValueProperty] = FirstValue.ToBinding();
            editor[!Vector4Editor<float>.SecondValueProperty] = SecondValue.ToBinding();
            editor[!Vector4Editor<float>.ThirdValueProperty] = ThirdValue.ToBinding();
            editor[!Vector4Editor<float>.FourthValueProperty] = FourthValue.ToBinding();
            editor[!Vector4Editor.IsUniformProperty] = IsUniformEditorEnabled.ToBinding();
            editor.ValueConfirmed += OnValueConfirmed;
            editor.ValueChanged += OnValueChanged;
        }
    }

    private void OnValueConfirmed(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (e is PropertyEditorValueChangedEventArgs<(float TopLeft, float TopRight, float BottomRight, float BottomLeft)> args)
        {
            SetValue(new Media.CornerRadius(args.OldValue.TopLeft, args.OldValue.TopRight, args.OldValue.BottomRight, args.OldValue.BottomLeft),
                     new Media.CornerRadius(args.NewValue.TopLeft, args.NewValue.TopRight, args.NewValue.BottomRight, args.NewValue.BottomLeft));
        }
    }

    private void OnValueChanged(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (sender is Vector4Editor<float> editor)
        {
            Media.CornerRadius coerced = SetCurrentValueAndGetCoerced(
                new Media.CornerRadius(editor.FirstValue, editor.SecondValue, editor.ThirdValue, editor.FourthValue));
            editor.FirstValue = coerced.TopLeft;
            editor.SecondValue = coerced.TopRight;
            editor.ThirdValue = coerced.BottomRight;
            editor.FourthValue = coerced.BottomLeft;
        }
    }
}
