using Avalonia;
using Avalonia.Interactivity;

using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.Helpers;
using Beutl.Media;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.PropertyEditors.ViewModels;

public sealed class GradingColorEditorViewModel : ValueEditorViewModel<GradingColor>, IConfigureLivePreview
{
    private readonly LiveEditDrag _drag = new();

    public GradingColorEditorViewModel(IPropertyAdapter<GradingColor> property)
        : base(property)
    {
    }

    public ReactivePropertySlim<bool> IsLivePreviewEnabled { get; } = new(true);

    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        if (visitor is GradingColorEditor editor && !Disposables.IsDisposed)
        {
            editor.Bind(GradingColorEditor.ValueProperty, Value.ToBinding())
                .DisposeWith(Disposables);
            editor.Bind(GradingColorEditor.IsLivePreviewEnabledProperty, IsLivePreviewEnabled.ToBinding())
                .DisposeWith(Disposables);
            editor.AddDisposableHandler(PropertyEditor.ValueConfirmedEvent, OnValueConfirmed)
                .DisposeWith(Disposables);
            editor.AddDisposableHandler(PropertyEditor.ValueChangedEvent, OnValueChanged)
                .DisposeWith(Disposables);
        }
        if (visitor is ColorGradingWheel wheel && !Disposables.IsDisposed)
        {
            wheel.Bind(ColorGradingWheel.ColorProperty, Value.ToBinding())
                .DisposeWith(Disposables);
            wheel.AddDisposableHandler(PropertyEditor.ValueConfirmedEvent, OnValueConfirmed)
                .DisposeWith(Disposables);
            wheel.AddDisposableHandler(PropertyEditor.ValueChangedEvent, OnValueChanged)
                .DisposeWith(Disposables);
        }
    }

    private void OnValueChanged(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (sender is GradingColorEditor editor)
        {
            editor.Value = SetCurrentValueAndGetCoerced(editor.Value);
        }
        else if (sender is ColorGradingWheel wheel)
        {
            // Only a drag on the wheel opens a session; typed values are confirmed as before.
            GradingColor coerced = default;
            _drag.Write(this, wheel.IsDragging ? wheel : null, wheel.StopDrag, () => coerced = SetCurrentValueAndGetCoerced(wheel.Color));
            wheel.Color = coerced;
        }
    }

    private void OnValueConfirmed(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        // A drag's session holds its writes; SetValue then finds the value in place and commits nothing more.
        _drag.Confirm();
        if (e.Expect<GradingColor>(sender) is { } args)
        {
            SetValue(args.OldValue, args.NewValue);
        }
    }
}
