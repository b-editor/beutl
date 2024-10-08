﻿using Avalonia;
using Avalonia.Interactivity;

using Beutl.Controls.PropertyEditors;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.ViewModels.Editors;

public sealed class ThicknessEditorViewModel : ValueEditorViewModel<Graphics.Thickness>, IConfigureUniformEditor
{
    public ThicknessEditorViewModel(IPropertyAdapter<Graphics.Thickness> property)
        : base(property)
    {
        FirstValue = Value
            .Select(x => x.Left)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);

        SecondValue = Value
            .Select(x => x.Top)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);

        ThirdValue = Value
            .Select(x => x.Right)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);

        FourthValue = Value
            .Select(x => x.Bottom)
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
        if (visitor is Vector4Editor<float> editor && !Disposables.IsDisposed)
        {
            editor.Bind(Vector4Editor<float>.FirstValueProperty, FirstValue.ToBinding())
                .DisposeWith(Disposables);
            editor.Bind(Vector4Editor<float>.SecondValueProperty, SecondValue.ToBinding())
                .DisposeWith(Disposables);
            editor.Bind(Vector4Editor<float>.ThirdValueProperty, ThirdValue.ToBinding())
                .DisposeWith(Disposables);
            editor.Bind(Vector4Editor<float>.FourthValueProperty, FourthValue.ToBinding())
                .DisposeWith(Disposables);
            editor.Bind(Vector4Editor.IsUniformProperty, IsUniformEditorEnabled.ToBinding())
                .DisposeWith(Disposables);
            editor.FirstHeader = Strings.Left;
            editor.SecondHeader = Strings.Top;
            editor.ThirdHeader = Strings.Right;
            editor.FourthHeader = Strings.Bottom;
            editor.AddDisposableHandler(PropertyEditor.ValueConfirmedEvent, OnValueConfirmed)
                .DisposeWith(Disposables);
            editor.AddDisposableHandler(PropertyEditor.ValueChangedEvent, OnValueChanged)
                .DisposeWith(Disposables);
        }
    }

    private void OnValueConfirmed(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (e is PropertyEditorValueChangedEventArgs<(float Left, float Top, float Right, float Bottom)> args)
        {
            SetValue(new Graphics.Thickness(args.OldValue.Left, args.OldValue.Top, args.OldValue.Right, args.OldValue.Bottom),
                     new Graphics.Thickness(args.NewValue.Left, args.NewValue.Top, args.NewValue.Right, args.NewValue.Bottom));
        }
    }

    private void OnValueChanged(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (sender is Vector4Editor<float> editor)
        {
            Graphics.Thickness coerced = SetCurrentValueAndGetCoerced(
                new Graphics.Thickness(editor.FirstValue, editor.SecondValue, editor.ThirdValue, editor.FourthValue));
            editor.FirstValue = coerced.Left;
            editor.SecondValue = coerced.Top;
            editor.ThirdValue = coerced.Right;
            editor.FourthValue = coerced.Bottom;
        }
    }
}
