using Avalonia;
using Avalonia.Interactivity;
using Beutl.Controls.PropertyEditors;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.ViewModels.Editors;

public sealed class RelativeRectEditorViewModel : ValueEditorViewModel<Graphics.RelativeRect>, IConfigureUniformEditor
{
    public RelativeRectEditorViewModel(IPropertyAdapter<Graphics.RelativeRect> property)
        : base(property)
    {
        FirstValue = Value
            .Select(x => x.Rect.X)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);

        SecondValue = Value
            .Select(x => x.Rect.Y)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);

        ThirdValue = Value
            .Select(x => x.Rect.Width)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);

        FourthValue = Value
            .Select(x => x.Rect.Height)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);

        UnitValue = Value
            .Select(x => x.Unit)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);
    }

    public ReadOnlyReactivePropertySlim<float> FirstValue { get; }

    public ReadOnlyReactivePropertySlim<float> SecondValue { get; }

    public ReadOnlyReactivePropertySlim<float> ThirdValue { get; }

    public ReadOnlyReactivePropertySlim<float> FourthValue { get; }

    public ReadOnlyReactivePropertySlim<Graphics.RelativeUnit> UnitValue { get; }

    public ReactivePropertySlim<bool> IsUniformEditorEnabled { get; } = new();

    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        if (visitor is RelativeRectEditor editor && !Disposables.IsDisposed)
        {
            editor.Bind(RelativeRectEditor.FirstValueProperty, FirstValue.ToBinding())
                .DisposeWith(Disposables);
            editor.Bind(RelativeRectEditor.SecondValueProperty, SecondValue.ToBinding())
                .DisposeWith(Disposables);
            editor.Bind(RelativeRectEditor.ThirdValueProperty, ThirdValue.ToBinding())
                .DisposeWith(Disposables);
            editor.Bind(RelativeRectEditor.FourthValueProperty, FourthValue.ToBinding())
                .DisposeWith(Disposables);
            editor.Bind(RelativeRectEditor.UnitProperty, UnitValue.ToBinding())
                .DisposeWith(Disposables);
            editor.Bind(Vector4Editor.IsUniformProperty, IsUniformEditorEnabled.ToBinding())
                .DisposeWith(Disposables);
            editor.AddDisposableHandler(PropertyEditor.ValueConfirmedEvent, OnValueConfirmed)
                .DisposeWith(Disposables);
            editor.AddDisposableHandler(PropertyEditor.ValueChangedEvent, OnValueChanged)
                .DisposeWith(Disposables);
        }
    }

    private void OnValueConfirmed(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (e is PropertyEditorValueChangedEventArgs<Graphics.RelativeRect> args)
        {
            SetValue(args.OldValue, args.NewValue);
        }
    }

    private void OnValueChanged(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (e is PropertyEditorValueChangedEventArgs<Graphics.RelativeRect> args
            && sender is RelativeRectEditor editor)
        {
            Graphics.RelativeRect coerced = SetCurrentValueAndGetCoerced(args.NewValue);
            editor.FirstValue = coerced.Rect.X;
            editor.SecondValue = coerced.Rect.Y;
            editor.ThirdValue = coerced.Rect.Width;
            editor.FourthValue = coerced.Rect.Height;
            editor.Unit = coerced.Unit;
        }
    }
}
