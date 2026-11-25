using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Services;
using Beutl.Media;

using Reactive.Bindings;

namespace Beutl.Editor.Components.PropertyEditors.ViewModels;

public sealed class GradingColorEditorViewModel : ValueEditorViewModel<GradingColor>, IConfigureLivePreview
{
    protected override bool CanEditUnderExpression => true;

    private readonly LiveEditDrag _drag = new();

    public GradingColorEditorViewModel(IPropertyAdapter<GradingColor> property)
        : base(property)
    {
    }

    public ReactivePropertySlim<bool> IsLivePreviewEnabled { get; } = new(true);

    public override void ConfirmEdit(GradingColor oldValue, GradingColor newValue)
    {
        if (_drag.Confirm()) return;
        base.ConfirmEdit(oldValue, newValue);
    }

    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        if (visitor is GradingColorEditor editor && !Disposables.IsDisposed)
        {
            editor.Bind(GradingColorEditor.IsLivePreviewEnabledProperty, IsLivePreviewEnabled.ToBinding())
                .DisposeWith(Disposables);
        }
        if (visitor is ColorGradingWheel wheel && !Disposables.IsDisposed)
        {
            // The wheel resets to the property's default, which for Gamma and Gain is not zero.
            if (GetDefaultValue() is GradingColor neutral)
                wheel.Neutral = neutral;

            wheel.Bind(ColorGradingWheel.ColorProperty, Value.ToBinding())
                .DisposeWith(Disposables);
            wheel.ConnectUnderlyingComponents(() => TryGetFocusedEditValue(out var color) ? color : null)
                .DisposeWith(Disposables);
            wheel.OpenComponentDragSession = (owner, stop) => OpenComponentDrag(wheel, owner, stop);
            wheel.AddDisposableHandler(PropertyEditor.ValueConfirmedEvent, OnWheelValueConfirmed)
                .DisposeWith(Disposables);
            wheel.AddDisposableHandler(PropertyEditor.ValueChangedEvent, OnWheelValueChanged)
                .DisposeWith(Disposables);
        }
    }

    private DragSession? OpenComponentDrag(ColorGradingWheel wheel, Control owner, Action stop)
    {
        if (IsDisposed)
            return null;

        DragSession? drag = _drag.Open(this, owner, () =>
        {
            stop();
            StopDrag(wheel);
        });
        // A deferred component edit belongs to the keyframe at the start of the drag.
        if (drag != null && !IsLivePreviewEnabled.Value && GetService(typeof(IEditorClock)) is IEditorClock clock)
            drag.CancelOnTimeChange(clock);
        return drag;
    }

    private void OnWheelValueChanged(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (sender is not ColorGradingWheel wheel || e.Expect<GradingColor>(sender) is not { } args)
            return;

        bool wheelDrag = wheel.IsDragging;
        Control? dragOwner = wheelDrag ? wheel : null;
        if (IsLivePreviewEnabled.Value)
        {
            // An RGB field's event carries the underlying input even when the wheel is showing the expression result.
            GradingColor coerced = default;
            _drag.Write(this, dragOwner, () => StopDrag(wheel),
                () => coerced = SetCurrentValueAndGetCoerced(args.NewValue, wheelDrag));
            // The pad and jog use the input to measure their relative change even when an expression shows another value.
            wheel.Color = wheelDrag ? GetIncrementValue(coerced) : coerced;
        }
        else if (wheelDrag || wheel.IsComponentDragging)
        {
            _drag.Defer(
                this,
                dragOwner,
                () => StopDrag(wheel),
                () =>
                {
                    GradingColor input = wheelDrag ? wheel.Color : args.NewValue;
                    wheel.Color = SetCurrentValueAndGetCoerced(input, wheelDrag);
                    EndValuePreview();
                },
                () => wheel.Color = Value.Value);
        }
    }

    private void OnWheelValueConfirmed(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (sender is not ColorGradingWheel wheel || e.Expect<GradingColor>(sender) is not { } args)
            return;

        // The session has already committed the underlying value, including deferred previews.
        if (!_drag.Confirm())
            SetValue(args.OldValue, args.NewValue);

        wheel.Color = Value.Value;
    }

    private void StopDrag(ColorGradingWheel wheel)
    {
        wheel.StopDrag();
        EndValuePreview();
        // A live drag left its input on the wheel; without live preview the wheel's color is what the drag applies.
        if (IsLivePreviewEnabled.Value)
            wheel.Color = Value.Value;
    }
}
