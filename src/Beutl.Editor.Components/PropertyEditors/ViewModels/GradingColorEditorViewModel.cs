using Avalonia;
using Avalonia.Interactivity;

using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.Helpers;
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
            wheel.AddDisposableHandler(PropertyEditor.ValueConfirmedEvent, OnWheelValueConfirmed)
                .DisposeWith(Disposables);
            wheel.AddDisposableHandler(PropertyEditor.ValueChangedEvent, OnWheelValueChanged)
                .DisposeWith(Disposables);
        }
    }

    private void OnWheelValueChanged(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (sender is not ColorGradingWheel wheel)
            return;

        if (IsLivePreviewEnabled.Value)
        {
            // Only a drag on the wheel opens a session; typed values are confirmed as before.
            GradingColor coerced = default;
            _drag.Write(this, wheel.IsDragging ? wheel : null, () => StopDrag(wheel),
                () => coerced = SetCurrentValueAndGetCoerced(wheel.Color, wheel.IsDragging));
            // During a drag the wheel keeps the input rather than an expression's result, so that moving back to where the
            // drag began is still a change; it shows the result again once the drag ends.
            wheel.Color = wheel.IsDragging ? GetIncrementValue(coerced) : coerced;
        }
        else if (wheel.IsDragging)
        {
            // Without live preview only the wheel shows the color until the drag ends, and typed values wait for their
            // confirmation. A drag still holds a session, so that Escape and Undo put the wheel back and playback start
            // applies the color as a release does.
            _drag.Defer(
                this,
                wheel,
                () => StopDrag(wheel),
                () =>
                {
                    wheel.Color = SetCurrentValueAndGetCoerced(wheel.Color);
                    EndValuePreview();
                },
                () => wheel.Color = Value.Value);
        }
    }

    private void OnWheelValueConfirmed(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        // The session has already committed the underlying value, including deferred previews.
        if (_drag.Confirm())
        {
            if (IsLivePreviewEnabled.Value && sender is ColorGradingWheel wheel)
                wheel.Color = Value.Value;
            return;
        }
        if (e.Expect<GradingColor>(sender) is { } args)
        {
            SetValue(args.OldValue, args.NewValue);
        }
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
