using Avalonia;
using Avalonia.Interactivity;

using Beutl.Controls.PropertyEditors;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.ViewModels.Editors;

public sealed class RelativePointEditorViewModel : ValueEditorViewModel<Graphics.RelativePoint>,IConfigureUniformEditor
{
    public RelativePointEditorViewModel(IAbstractProperty<Graphics.RelativePoint> property)
        : base(property)
    {
        FirstValue = Value
            .Select(x => x.Point.X)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);

        SecondValue = Value
            .Select(x => x.Point.Y)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);

        UnitValue = Value
            .Select(x => x.Unit)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);
    }

    public ReadOnlyReactivePropertySlim<float> FirstValue { get; }

    public ReadOnlyReactivePropertySlim<float> SecondValue { get; }

    public ReadOnlyReactivePropertySlim<Graphics.RelativeUnit> UnitValue { get; }

    public ReactivePropertySlim<bool> IsUniformEditorEnabled { get; } = new();

    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        if (visitor is RelativePointEditor editor && !Disposables.IsDisposed)
        {
            editor.Bind(RelativePointEditor.FirstValueProperty, FirstValue.ToBinding())
                .DisposeWith(Disposables);
            editor.Bind(RelativePointEditor.SecondValueProperty, SecondValue.ToBinding())
                .DisposeWith(Disposables);
            editor.Bind(RelativePointEditor.UnitProperty, UnitValue.ToBinding())
                .DisposeWith(Disposables);
            editor.Bind(Vector2Editor.IsUniformProperty, IsUniformEditorEnabled.ToBinding())
                .DisposeWith(Disposables);
            editor.AddDisposableHandler(PropertyEditor.ValueConfirmedEvent, OnValueConfirmed)
                .DisposeWith(Disposables);
            editor.AddDisposableHandler(PropertyEditor.ValueChangedEvent, OnValueChanged)
                .DisposeWith(Disposables);
        }
    }

    private void OnValueConfirmed(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (e is PropertyEditorValueChangedEventArgs<Graphics.RelativePoint> args)
        {
            SetValue(args.OldValue, args.NewValue);
        }
    }

    private void OnValueChanged(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (e is PropertyEditorValueChangedEventArgs<Graphics.RelativePoint> args
            && sender is RelativePointEditor editor)
        {
            Graphics.RelativePoint coerced = SetCurrentValueAndGetCoerced(args.NewValue);
            editor.FirstValue = coerced.Point.X;
            editor.SecondValue = coerced.Point.Y;
            editor.Unit = coerced.Unit;
        }
    }
}
