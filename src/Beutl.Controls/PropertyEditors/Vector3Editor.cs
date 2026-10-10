using System.Globalization;
using System.Numerics;
using System.Reactive.Disposables;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Beutl.Controls.PropertyEditors;

public class Vector3Editor<TElement> : Vector3Editor
    where TElement : INumber<TElement>
{
    public static readonly DirectProperty<Vector3Editor<TElement>, TElement> FirstValueProperty =
        Vector4Editor<TElement>.FirstValueProperty.AddOwner<Vector3Editor<TElement>>(
            o => o.FirstValue,
            (o, v) => o.FirstValue = v);

    public static readonly DirectProperty<Vector3Editor<TElement>, TElement> SecondValueProperty =
        Vector4Editor<TElement>.SecondValueProperty.AddOwner<Vector3Editor<TElement>>(
            o => o.SecondValue,
            (o, v) => o.SecondValue = v);

    public static readonly DirectProperty<Vector3Editor<TElement>, TElement> ThirdValueProperty =
        Vector4Editor<TElement>.ThirdValueProperty.AddOwner<Vector3Editor<TElement>>(
            o => o.ThirdValue,
            (o, v) => o.ThirdValue = v);

    public static readonly StyledProperty<TElement> LargeChangeProperty =
        Vector4Editor<TElement>.LargeChangeProperty.AddOwner<Vector3Editor<TElement>>();

    public static readonly StyledProperty<TElement> SmallChangeProperty =
        Vector4Editor<TElement>.SmallChangeProperty.AddOwner<Vector3Editor<TElement>>();

    private readonly CompositeDisposable _disposables = [];
    private TElement _firstValue = TElement.Zero;
    private TElement _oldFirstValue = TElement.Zero;
    private TElement _secondValue = TElement.Zero;
    private TElement _oldSecondValue = TElement.Zero;
    private TElement _thirdValue = TElement.Zero;
    private TElement _oldThirdValue = TElement.Zero;

    private TextBlock? _headerText;
    private HeaderScrubGesture _scrub;

    public Vector3Editor()
    {
        FirstHeader = "0";
        SecondHeader = "0";
        ThirdHeader = "0";
    }

    public TElement FirstValue
    {
        get => _firstValue;
        set
        {
            if (SetAndRaise(FirstValueProperty, ref _firstValue, value))
            {
                FirstText = NumberEditorHelper.Format(value, NumberFormat);
            }
        }
    }

    public TElement SecondValue
    {
        get => _secondValue;
        set
        {
            if (SetAndRaise(SecondValueProperty, ref _secondValue, value))
            {
                SecondText = NumberEditorHelper.Format(value, NumberFormat);
            }
        }
    }

    public TElement ThirdValue
    {
        get => _thirdValue;
        set
        {
            if (SetAndRaise(ThirdValueProperty, ref _thirdValue, value))
            {
                ThirdText = NumberEditorHelper.Format(value, NumberFormat);
            }
        }
    }

    public TElement LargeChange
    {
        get => GetValue(LargeChangeProperty);
        set => SetValue(LargeChangeProperty, value);
    }

    public TElement SmallChange
    {
        get => GetValue(SmallChangeProperty);
        set => SetValue(SmallChangeProperty, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        _disposables.Clear();
        base.OnApplyTemplate(e);
        FirstText = NumberEditorHelper.Format(_firstValue, NumberFormat);
        SecondText = NumberEditorHelper.Format(_secondValue, NumberFormat);
        ThirdText = NumberEditorHelper.Format(_thirdValue, NumberFormat);

        var valueHandlers = new ComponentValueHandlers(
            OnInnerTextBoxGotFocus, OnInnerTextBoxLostFocus, OnInnerTextBoxTextChanged, OnInnerTextBoxPointerWheelChanged);
        valueHandlers.Subscribe(InnerFirstTextBox, _disposables);
        valueHandlers.Subscribe(InnerSecondTextBox, _disposables);
        valueHandlers.Subscribe(InnerThirdTextBox, _disposables);

        var headerHandlers = new ScrubHeaderHandlers(
            OnTextBlockPointerPressed, OnTextBlockPointerReleased, OnTextBlockPointerMoved,
            OnTextBlockPointerCaptureLost, RoutingStrategies.Tunnel);
        headerHandlers.Subscribe(FirstHeaderTextBlock, _disposables);
        headerHandlers.Subscribe(SecondHeaderTextBlock, _disposables);
        headerHandlers.Subscribe(ThirdHeaderTextBlock, _disposables);
        _headerText = e.NameScope.Find<TextBlock>("PART_HeaderTextBlock");
        headerHandlers.Subscribe(_headerText, _disposables);

        UpdateErrors();
    }

    private void OnTextBlockPointerMoved(object? sender, PointerEventArgs e)
    {
        // A move with the button up means the release never reached the header.
        if (_scrub.IsActive && !e.Properties.IsLeftButtonPressed)
        {
            CompleteScrub();
            return;
        }

        if (!(InnerFirstTextBox.IsKeyboardFocusWithin
            || InnerSecondTextBox?.IsKeyboardFocusWithin == true
            || InnerThirdTextBox?.IsKeyboardFocusWithin == true)
            && _scrub.IsActive
            && sender is TextBlock headerText)
        {
            TElement delta = _scrub.NextDelta<TElement>(headerText, e) * SmallChange;

            var newValues = (FirstValue, SecondValue, ThirdValue);
            var oldValues = (FirstValue, SecondValue, ThirdValue);
            switch (headerText.Name)
            {
                case "PART_HeaderFirstTextBlock":
                    newValues.FirstValue = NumberEditorHelper.AddPreservingScale(newValues.FirstValue, delta);
                    break;
                case "PART_HeaderSecondTextBlock":
                    newValues.SecondValue = NumberEditorHelper.AddPreservingScale(newValues.SecondValue, delta);
                    break;
                case "PART_HeaderThirdTextBlock":
                    newValues.ThirdValue = NumberEditorHelper.AddPreservingScale(newValues.ThirdValue, delta);
                    break;
                case "PART_HeaderTextBlock":
                    newValues.FirstValue = NumberEditorHelper.AddPreservingScale(newValues.FirstValue, delta);
                    newValues.SecondValue = NumberEditorHelper.AddPreservingScale(newValues.SecondValue, delta);
                    newValues.ThirdValue = NumberEditorHelper.AddPreservingScale(newValues.ThirdValue, delta);
                    break;
                default:
                    break;
            }

            (FirstValue, SecondValue, ThirdValue) = newValues;
            RaiseEvent(new PropertyEditorValueChangedEventArgs<(TElement, TElement, TElement)>(
                newValues, oldValues, ValueChangedEvent));

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
        RaiseConfirmedIfChanged();
    }

    private void OnTextBlockPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is TextBlock headerText)
        {
            PointerPoint pointerPoint = e.GetCurrentPoint(headerText);
            if (pointerPoint.Properties.IsLeftButtonPressed
                && !DataValidationErrors.GetHasErrors(this))
            {
                SnapshotOldValues();
                _scrub.Begin(headerText, pointerPoint.Position);
                e.Handled = true;
            }
        }
    }

    private void OnInnerTextBoxGotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (!DataValidationErrors.GetHasErrors(this))
        {
            SnapshotOldValues();
        }
    }

    private void OnInnerTextBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        if (!DataValidationErrors.GetHasErrors(this))
        {
            RaiseConfirmedIfChanged();
        }
    }

    private void SnapshotOldValues()
    {
        _oldFirstValue = FirstValue;
        _oldSecondValue = SecondValue;
        _oldThirdValue = ThirdValue;
    }

    private void RaiseConfirmedIfChanged()
    {
        if (FirstValue != _oldFirstValue
            || SecondValue != _oldSecondValue
            || ThirdValue != _oldThirdValue)
        {
            RaiseEvent(new PropertyEditorValueChangedEventArgs<(TElement, TElement, TElement)>(
                (FirstValue, SecondValue, ThirdValue),
                (_oldFirstValue, _oldSecondValue, _oldThirdValue),
                ValueConfirmedEvent));
        }
    }

    private void OnInnerTextBoxTextChanged(TextBox sender, string? newValue, string? oldValue)
    {
        if (sender.IsKeyboardFocusWithin
            && NumberEditorHelper.TryParseEdit(newValue, oldValue, out TElement? newValue2, out TElement? oldValue2))
        {
            var newValues = (FirstValue, SecondValue, ThirdValue);
            var oldValues = (FirstValue, SecondValue, ThirdValue);
            if (IsUniform)
            {
                FirstValue = SecondValue = ThirdValue = newValue2;
                newValues = (newValue2, newValue2, newValue2);
                oldValues = (oldValue2, oldValue2, oldValue2);
            }
            else
            {
                switch (sender.Name)
                {
                    case "PART_InnerFirstTextBox":
                        FirstValue = newValue2;
                        newValues.FirstValue = newValue2;
                        oldValues.FirstValue = oldValue2;
                        break;
                    case "PART_InnerSecondTextBox":
                        SecondValue = newValue2;
                        newValues.SecondValue = newValue2;
                        oldValues.SecondValue = oldValue2;
                        break;
                    case "PART_InnerThirdTextBox":
                        ThirdValue = newValue2;
                        newValues.ThirdValue = newValue2;
                        oldValues.ThirdValue = oldValue2;
                        break;
                    default:
                        break;
                }
            }

            RaiseEvent(new PropertyEditorValueChangedEventArgs<(TElement, TElement, TElement)>(
                newValues, oldValues, ValueChangedEvent));
        }

        UpdateErrors();
    }

    private void UpdateErrors()
    {
        DataValidationMessages.UpdateInvalidString(
            this,
            TElement.TryParse(InnerFirstTextBox.Text, CultureInfo.CurrentCulture, out _)
                && (IsUniform
                || (TElement.TryParse(InnerSecondTextBox?.Text, CultureInfo.CurrentCulture, out _)
                && TElement.TryParse(InnerThirdTextBox?.Text, CultureInfo.CurrentCulture, out _))));
    }

    private void OnInnerTextBoxPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (!DataValidationErrors.GetHasErrors(this)
            && sender is TextBox textBox
            && textBox.IsKeyboardFocusWithin
            && TElement.TryParse(textBox.Text, CultureInfo.CurrentCulture, out TElement? value)
            && value is not null)
        {
            value = NumberEditorHelper.StepByWheel(value, e, LargeChange, SmallChange);

            if (IsUniform)
            {
                FirstValue = SecondValue = ThirdValue = value;
            }
            else
            {
                switch (textBox.Name)
                {
                    case "PART_InnerFirstTextBox":
                        FirstValue = value;
                        break;
                    case "PART_InnerSecondTextBox":
                        SecondValue = value;
                        break;
                    case "PART_InnerThirdTextBox":
                        ThirdValue = value;
                        break;
                    default:
                        break;
                }
            }

            e.Handled = true;
        }
    }
}

[PseudoClasses(
    FocusAnyTextBox, FocusFirstTextBox, FocusSecondTextBox, FocusThirdTextBox,
    BorderPointerOver, Uniform)]
[TemplatePart("PART_InnerFirstTextBox", typeof(TextBox))]
[TemplatePart("PART_InnerSecondTextBox", typeof(TextBox))]
[TemplatePart("PART_InnerThirdTextBox", typeof(TextBox))]
[TemplatePart("PART_HeaderFirstTextBlock", typeof(TextBlock))]
[TemplatePart("PART_HeaderSecondTextBlock", typeof(TextBlock))]
[TemplatePart("PART_HeaderThirdTextBlock", typeof(TextBlock))]
[TemplatePart("PART_BackgroundBorder", typeof(Border))]
public class Vector3Editor : PropertyEditor
{
    public static readonly DirectProperty<Vector3Editor, string> FirstTextProperty =
        Vector4Editor.FirstTextProperty.AddOwner<Vector3Editor>(
            o => o.FirstText,
            (o, v) => o.FirstText = v);

    public static readonly DirectProperty<Vector3Editor, string> SecondTextProperty =
        Vector4Editor.SecondTextProperty.AddOwner<Vector3Editor>(
            o => o.SecondText,
            (o, v) => o.SecondText = v);

    public static readonly DirectProperty<Vector3Editor, string> ThirdTextProperty =
        Vector4Editor.ThirdTextProperty.AddOwner<Vector3Editor>(
            o => o.ThirdText,
            (o, v) => o.ThirdText = v);

    public static readonly StyledProperty<string?> FirstHeaderProperty =
        Vector4Editor.FirstHeaderProperty.AddOwner<Vector3Editor>();

    public static readonly StyledProperty<string?> SecondHeaderProperty =
        Vector4Editor.SecondHeaderProperty.AddOwner<Vector3Editor>();

    public static readonly StyledProperty<string?> ThirdHeaderProperty =
        Vector4Editor.ThirdHeaderProperty.AddOwner<Vector3Editor>();

    public static readonly StyledProperty<bool> IsUniformProperty =
        Vector4Editor.IsUniformProperty.AddOwner<Vector3Editor>();

    public static readonly StyledProperty<string?> NumberFormatProperty =
        Vector4Editor.NumberFormatProperty.AddOwner<Vector3Editor>();

    private const string FocusAnyTextBox = ":focus-any-textbox";
    private const string FocusFirstTextBox = ":focus-1st-textbox";
    private const string FocusSecondTextBox = ":focus-2nd-textbox";
    private const string FocusThirdTextBox = ":focus-3rd-textbox";
    private const string BorderPointerOver = ":border-pointerover";
    private const string Uniform = ":uniform";
    private readonly CompositeDisposable _disposables = [];
    private Border? _backgroundBorder;
    private string _firstText = string.Empty;
    private string _secondText = string.Empty;
    private string _thirdText = string.Empty;

    public string FirstText
    {
        get => _firstText;
        set => SetAndRaise(FirstTextProperty, ref _firstText, value);
    }

    public string SecondText
    {
        get => _secondText;
        set => SetAndRaise(SecondTextProperty, ref _secondText, value);
    }

    public string ThirdText
    {
        get => _thirdText;
        set => SetAndRaise(ThirdTextProperty, ref _thirdText, value);
    }

    public string? FirstHeader
    {
        get => GetValue(FirstHeaderProperty);
        set => SetValue(FirstHeaderProperty, value);
    }

    public string? SecondHeader
    {
        get => GetValue(SecondHeaderProperty);
        set => SetValue(SecondHeaderProperty, value);
    }

    public string? ThirdHeader
    {
        get => GetValue(ThirdHeaderProperty);
        set => SetValue(ThirdHeaderProperty, value);
    }

    public bool IsUniform
    {
        get => GetValue(IsUniformProperty);
        set => SetValue(IsUniformProperty, value);
    }

    public string? NumberFormat
    {
        get => GetValue(NumberFormatProperty);
        set => SetValue(NumberFormatProperty, value);
    }

    protected TextBox InnerFirstTextBox { get; private set; } = null!;

    protected TextBox? InnerSecondTextBox { get; private set; }

    protected TextBox? InnerThirdTextBox { get; private set; }

    protected TextBlock? FirstHeaderTextBlock { get; private set; }

    protected TextBlock? SecondHeaderTextBlock { get; private set; }

    protected TextBlock? ThirdHeaderTextBlock { get; private set; }

    protected override Type StyleKeyOverride => typeof(Vector3Editor);

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        _disposables.Clear();
        base.OnApplyTemplate(e);
        InnerFirstTextBox = e.NameScope.Get<TextBox>("PART_InnerFirstTextBox");
        InnerSecondTextBox = e.NameScope.Find<TextBox>("PART_InnerSecondTextBox");
        InnerThirdTextBox = e.NameScope.Find<TextBox>("PART_InnerThirdTextBox");
        FirstHeaderTextBlock = e.NameScope.Find<TextBlock>("PART_HeaderFirstTextBlock");
        SecondHeaderTextBlock = e.NameScope.Find<TextBlock>("PART_HeaderSecondTextBlock");
        ThirdHeaderTextBlock = e.NameScope.Find<TextBlock>("PART_HeaderThirdTextBlock");
        _backgroundBorder = e.NameScope.Find<Border>("PART_BackgroundBorder");

        var hoverHandlers = new ComponentHoverHandlers(OnInnerTextBoxGotFocus, OnInnerTextBoxLostFocus, IsPointerOverChanged);
        hoverHandlers.SubscribeTextBox(InnerFirstTextBox, _disposables);
        hoverHandlers.SubscribeTextBox(InnerSecondTextBox, _disposables);
        hoverHandlers.SubscribeTextBox(InnerThirdTextBox, _disposables);
        hoverHandlers.SubscribePointerOver(FirstHeaderTextBlock, _disposables);
        hoverHandlers.SubscribePointerOver(SecondHeaderTextBlock, _disposables);
        hoverHandlers.SubscribePointerOver(ThirdHeaderTextBlock, _disposables);

        hoverHandlers.SubscribePointerOver(_backgroundBorder, _disposables);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsUniformProperty)
        {
            PseudoClasses.Set(Uniform, change.GetNewValue<bool>());
        }
    }

    private void IsPointerOverChanged(bool obj)
    {
        if (_backgroundBorder?.IsPointerOver == true
            || InnerFirstTextBox.IsPointerOver
            || InnerSecondTextBox?.IsPointerOver == true
            || InnerThirdTextBox?.IsPointerOver == true
            || FirstHeaderTextBlock?.IsPointerOver == true
            || SecondHeaderTextBlock?.IsPointerOver == true
            || ThirdHeaderTextBlock?.IsPointerOver == true)
        {
            PseudoClasses.Add(BorderPointerOver);
        }
        else
        {
            PseudoClasses.Remove(BorderPointerOver);
        }
    }

    private void OnInnerTextBoxGotFocus(object? sender, FocusChangedEventArgs e)
    {
        UpdateFocusState();
    }

    private void OnInnerTextBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        UpdateFocusState();
    }

    private void UpdateFocusState()
    {
        PseudoClasses.Remove(FocusFirstTextBox);
        PseudoClasses.Remove(FocusSecondTextBox);
        PseudoClasses.Remove(FocusThirdTextBox);
        if (InnerFirstTextBox.IsFocused)
            PseudoClasses.Add(FocusFirstTextBox);
        else if (InnerSecondTextBox?.IsFocused == true)
            PseudoClasses.Add(FocusSecondTextBox);
        else if (InnerThirdTextBox?.IsFocused == true)
            PseudoClasses.Add(FocusThirdTextBox);

        if (InnerFirstTextBox.IsFocused
            || InnerSecondTextBox?.IsFocused == true
            || InnerThirdTextBox?.IsFocused == true)
        {
            PseudoClasses.Add(FocusAnyTextBox);
        }
        else
        {
            PseudoClasses.Remove(FocusAnyTextBox);
        }
    }
}
