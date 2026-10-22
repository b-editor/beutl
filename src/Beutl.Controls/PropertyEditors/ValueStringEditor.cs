using System.Diagnostics.CodeAnalysis;
using System.Reactive.Disposables;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Beutl.Extensibility;
using Beutl.Reactive;

namespace Beutl.Controls.PropertyEditors;

// The text box editing of NumberEditor, RationalEditor and MediaTimeEditor; they parse, format and step the value.
public abstract class ValueStringEditor<TValue> : StringEditorBase, IValueEditorControl<TValue>
{
    private readonly CompositeDisposable _disposables = [];
    private readonly bool _canScrub;
    private TValue _value;
    private TValue _oldValue;
    private Control? _editSource;
    private HeaderScrubGesture _scrub;
    private TextBlock? _headerText;

    // canScrub makes dragging the header change the value.
    private protected ValueStringEditor(TValue value, bool canScrub)
    {
        _value = value;
        _oldValue = value;
        _canScrub = canScrub;
    }

    public TValue Value
    {
        get => _value;
        set
        {
            if (SetAndRaise(ValuePropertyCore, ref _value, value)
                && (_editSource == null || _editSource != InnerTextBox))
            {
                Text = Format(value);
            }
        }
    }

    private protected abstract DirectPropertyBase<TValue> ValuePropertyCore { get; }

    public void Connect(IValueEditorContext<TValue> context) => ConnectValue(context, ValuePropertyCore);

    private protected abstract string Format(TValue value);

    // The type's != operator, under which NaN differs from itself.
    private protected abstract bool Differs(TValue x, TValue y);

    private protected abstract bool TryParse(string? text, [MaybeNullWhen(false)] out TValue value);

    private protected abstract TValue StepByWheel(TValue value, PointerWheelEventArgs e);

    private protected virtual TValue Scrub(TValue value, ref HeaderScrubGesture scrub, Visual header, PointerEventArgs e)
    {
        return value;
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        _disposables.Clear();
        base.OnApplyTemplate(e);
        if (InnerTextBox == null) return;
        InnerTextBox.AddDisposableHandler(PointerWheelChangedEvent, OnTextBoxPointerWheelChanged, RoutingStrategies.Tunnel)
            .DisposeWith(_disposables);

        if (!_canScrub) return;

        _headerText = e.NameScope.Find<TextBlock>("PART_HeaderTextBlock");
        new ScrubHeaderHandlers(
                OnTextBlockPointerPressed, OnTextBlockPointerReleased, OnTextBlockPointerMoved,
                OnTextBlockPointerCaptureLost, RoutingStrategies.Direct | RoutingStrategies.Bubble)
            .Subscribe(_headerText, _disposables);
    }

    private void OnTextBlockPointerMoved(object? sender, PointerEventArgs e)
    {
        // A move with the button up means the release never reached the header.
        if (_scrub.IsActive && !e.Properties.IsLeftButtonPressed)
        {
            CompleteScrub();
            return;
        }

        if (InnerTextBox == null) return;
        if (_headerText is not { } headerText) return;
        if (!InnerTextBox.IsKeyboardFocusWithin && _scrub.IsActive)
        {
            TValue oldValue = Value;
            TValue newValue = Scrub(oldValue, ref _scrub, headerText, e);
            if (Differs(newValue, oldValue))
            {
                Value = newValue;
                RaiseEvent(new PropertyEditorValueChangedEventArgs<TValue>(newValue, oldValue, ValueChangedEvent));
            }

            e.Handled = true;

            UpdateErrors();
        }
    }

    private void OnTextBlockPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_scrub.IsActive)
        {
            CompleteScrub();
            e.Handled = true;
        }
    }

    // Another window, a dialog or a rebuilt header took the pointer; end as a release would.
    private void OnTextBlockPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        CompleteScrub();
    }

    private void CompleteScrub()
    {
        if (!_scrub.IsActive) return;

        // Ended before confirming, so a confirmation that rebuilds the header cannot end it twice.
        _scrub.End();
        if (Differs(Value, _oldValue))
        {
            RaiseEvent(new PropertyEditorValueChangedEventArgs<TValue>(Value, _oldValue, ValueConfirmedEvent));
        }
    }

    private void OnTextBlockPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (InnerTextBox == null) return;
        if (_headerText is not { } headerText) return;
        PointerPoint pointerPoint = e.GetCurrentPoint(headerText);
        if (pointerPoint.Properties.IsLeftButtonPressed
            && !DataValidationErrors.GetHasErrors(InnerTextBox))
        {
            _oldValue = Value;
            _scrub.Begin(headerText, pointerPoint.Position);
            e.Handled = true;
        }
    }

    protected override void OnTextBoxGotFocus(FocusChangedEventArgs e)
    {
        if (InnerTextBox == null) return;
        if (!DataValidationErrors.GetHasErrors(InnerTextBox))
        {
            _oldValue = Value;
        }
    }

    protected override void OnTextBoxLostFocus(RoutedEventArgs e)
    {
        if (InnerTextBox == null) return;
        if (!DataValidationErrors.GetHasErrors(InnerTextBox))
        {
            Text = Format(Value);
            if (Differs(Value, _oldValue))
            {
                RaiseEvent(new PropertyEditorValueChangedEventArgs<TValue>(Value, _oldValue, ValueConfirmedEvent));
            }
        }
    }

    protected override void OnTextBoxTextChanged(string newValue, string oldValue)
    {
        if (_editSource == null
            && InnerTextBox?.IsKeyboardFocusWithin == true
            && TryParseEdit(newValue, oldValue, out TValue? newValue2, out TValue? oldValue2))
        {
            using var edit = new PropertyEditorEditScope(ref _editSource, InnerTextBox);
            Value = newValue2;
            RaiseEvent(new PropertyEditorValueChangedEventArgs<TValue>(newValue2, oldValue2, ValueChangedEvent));
        }

        UpdateErrors();
    }

    // An edit applies when the new text parses and either the old text did not parse or the value changed.
    private bool TryParseEdit(
        string newText,
        string oldText,
        [MaybeNullWhen(false)] out TValue newValue,
        [MaybeNullWhen(false)] out TValue oldValue)
    {
        if (TryParse(newText, out newValue))
        {
            if (!TryParse(oldText, out oldValue))
            {
                oldValue = newValue;
                return true;
            }

            return Differs(newValue, oldValue);
        }

        oldValue = default;
        return false;
    }

    private void UpdateErrors()
    {
        if (InnerTextBox == null) return;
        DataValidationMessages.UpdateInvalidString(InnerTextBox, TryParse(InnerTextBox.Text, out _));
    }

    private void OnTextBoxPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (InnerTextBox == null) return;
        if (!DataValidationErrors.GetHasErrors(InnerTextBox)
            && InnerTextBox.IsKeyboardFocusWithin
            && TryParse(InnerTextBox.Text, out _))
        {
            using var edit = new PropertyEditorEditScope(ref _editSource, this);
            TValue oldValue = Value;
            TValue newValue = StepByWheel(oldValue, e);
            Value = newValue;
            if (Differs(newValue, oldValue))
                RaiseEvent(new PropertyEditorValueChangedEventArgs<TValue>(newValue, oldValue, ValueChangedEvent));
            Text = Format(Value);
            e.Handled = true;
        }
    }
}
