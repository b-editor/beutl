using System.Globalization;
using System.Numerics;
using System.Reactive.Disposables;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Beutl.Controls.PropertyEditors;

public class Vector4Editor<TElement> : Vector4Editor
    where TElement : INumber<TElement>
{
    public static readonly DirectProperty<Vector4Editor<TElement>, TElement> FirstValueProperty =
        AvaloniaProperty.RegisterDirect<Vector4Editor<TElement>, TElement>(
            nameof(FirstValue),
            o => o.FirstValue,
            (o, v) => o.FirstValue = v,
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly DirectProperty<Vector4Editor<TElement>, TElement> SecondValueProperty =
        AvaloniaProperty.RegisterDirect<Vector4Editor<TElement>, TElement>(
            nameof(SecondValue),
            o => o.SecondValue,
            (o, v) => o.SecondValue = v,
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly DirectProperty<Vector4Editor<TElement>, TElement> ThirdValueProperty =
        AvaloniaProperty.RegisterDirect<Vector4Editor<TElement>, TElement>(
            nameof(ThirdValue),
            o => o.ThirdValue,
            (o, v) => o.ThirdValue = v,
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly DirectProperty<Vector4Editor<TElement>, TElement> FourthValueProperty =
        AvaloniaProperty.RegisterDirect<Vector4Editor<TElement>, TElement>(
            nameof(FourthValue),
            o => o.FourthValue,
            (o, v) => o.FourthValue = v,
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<TElement> LargeChangeProperty =
        AvaloniaProperty.Register<Vector4Editor<TElement>, TElement>(
            nameof(LargeChange),
            defaultValue: TElement.CreateTruncating(10));

    public static readonly StyledProperty<TElement> SmallChangeProperty =
        AvaloniaProperty.Register<Vector4Editor<TElement>, TElement>(
            nameof(SmallChange),
            defaultValue: TElement.One);

    private readonly CompositeDisposable _disposables = [];
    private TElement _firstValue = TElement.Zero;
    private TElement _oldFirstValue = TElement.Zero;
    private TElement _secondValue = TElement.Zero;
    private TElement _oldSecondValue = TElement.Zero;
    private TElement _thirdValue = TElement.Zero;
    private TElement _oldThirdValue = TElement.Zero;
    private TElement _fourthValue = TElement.Zero;
    private TElement _oldFourthValue = TElement.Zero;
    private TextBlock? _headerText;
    private HeaderScrubGesture _scrub;

    public Vector4Editor()
    {
        FirstHeader = "0";
        SecondHeader = "0";
        ThirdHeader = "0";
        FourthHeader = "0";
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

    public TElement FourthValue
    {
        get => _fourthValue;
        set
        {
            if (SetAndRaise(FourthValueProperty, ref _fourthValue, value))
            {
                FourthText = NumberEditorHelper.Format(value, NumberFormat);
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
        FourthText = NumberEditorHelper.Format(_fourthValue, NumberFormat);

        var valueHandlers = new ComponentValueHandlers(
            OnInnerTextBoxGotFocus, OnInnerTextBoxLostFocus, OnInnerTextBoxTextChanged, OnInnerTextBoxPointerWheelChanged);
        valueHandlers.Subscribe(InnerFirstTextBox, _disposables);
        valueHandlers.Subscribe(InnerSecondTextBox, _disposables);
        valueHandlers.Subscribe(InnerThirdTextBox, _disposables);
        valueHandlers.Subscribe(InnerFourthTextBox, _disposables);

        _headerText = e.NameScope.Find<TextBlock>("PART_HeaderTextBlock");
        new ScrubHeaderHandlers(
                OnTextBlockPointerPressed, OnTextBlockPointerReleased, OnTextBlockPointerMoved,
                OnTextBlockPointerCaptureLost, RoutingStrategies.Direct | RoutingStrategies.Bubble)
            .Subscribe(_headerText, _disposables);

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

        if (_headerText is not { } headerText) return;
        if (!(InnerFirstTextBox.IsKeyboardFocusWithin
            || InnerSecondTextBox?.IsKeyboardFocusWithin == true
            || InnerThirdTextBox?.IsKeyboardFocusWithin == true
            || InnerFourthTextBox?.IsKeyboardFocusWithin == true)
            && _scrub.IsActive)
        {
            TElement delta = _scrub.NextDelta<TElement>(headerText, e) * SmallChange;

            var newValues = (
                NumberEditorHelper.AddPreservingScale(FirstValue, delta),
                NumberEditorHelper.AddPreservingScale(SecondValue, delta),
                NumberEditorHelper.AddPreservingScale(ThirdValue, delta),
                NumberEditorHelper.AddPreservingScale(FourthValue, delta));
            var oldValues = (FirstValue, SecondValue, ThirdValue, FourthValue);

            (FirstValue, SecondValue, ThirdValue, FourthValue) = newValues;
            RaiseEvent(new PropertyEditorValueChangedEventArgs<(TElement, TElement, TElement, TElement)>(
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
        if (FirstValue != _oldFirstValue
            || SecondValue != _oldSecondValue
            || ThirdValue != _oldThirdValue
            || FourthValue != _oldFourthValue)
        {
            RaiseEvent(new PropertyEditorValueChangedEventArgs<(TElement, TElement, TElement, TElement)>(
                (FirstValue, SecondValue, ThirdValue, FourthValue),
                (_oldFirstValue, _oldSecondValue, _oldThirdValue, _oldFourthValue),
                ValueConfirmedEvent));
        }
    }

    private void OnTextBlockPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_headerText is not { } headerText) return;
        PointerPoint pointerPoint = e.GetCurrentPoint(headerText);
        if (pointerPoint.Properties.IsLeftButtonPressed
            && !DataValidationErrors.GetHasErrors(this))
        {
            _oldFirstValue = FirstValue;
            _oldSecondValue = SecondValue;
            _oldThirdValue = ThirdValue;
            _oldFourthValue = FourthValue;
            _scrub.Begin(headerText, pointerPoint.Position);
            e.Handled = true;
        }
    }

    private void OnInnerTextBoxGotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (!DataValidationErrors.GetHasErrors(this))
        {
            _oldFirstValue = FirstValue;
            _oldSecondValue = SecondValue;
            _oldThirdValue = ThirdValue;
            _oldFourthValue = FourthValue;
        }
    }

    private void OnInnerTextBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        if (!DataValidationErrors.GetHasErrors(this))
        {
            if (FirstValue != _oldFirstValue
                || SecondValue != _oldSecondValue
                || ThirdValue != _oldThirdValue
                || FourthValue != _oldFourthValue)
            {
                RaiseEvent(new PropertyEditorValueChangedEventArgs<(TElement, TElement, TElement, TElement)>(
                    (FirstValue, SecondValue, ThirdValue, FourthValue),
                    (_oldFirstValue, _oldSecondValue, _oldThirdValue, _oldFourthValue),
                    ValueConfirmedEvent));
            }
        }
    }

    private void OnInnerTextBoxTextChanged(TextBox sender, string? newValue, string? oldValue)
    {
        if (sender.IsKeyboardFocusWithin
            && NumberEditorHelper.TryParseEdit(newValue, oldValue, out TElement? newValue2, out TElement? oldValue2))
        {
            var newValues = (FirstValue, SecondValue, ThirdValue, FourthValue);
            var oldValues = (FirstValue, SecondValue, ThirdValue, FourthValue);
            if (IsUniform)
            {
                FirstValue = SecondValue = ThirdValue = FourthValue = newValue2;
                newValues = (newValue2, newValue2, newValue2, newValue2);
                oldValues = (oldValue2, oldValue2, oldValue2, oldValue2);
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
                    case "PART_InnerFourthTextBox":
                        FourthValue = newValue2;
                        newValues.FourthValue = newValue2;
                        oldValues.FourthValue = oldValue2;
                        break;
                    default:
                        break;
                }
            }

            RaiseEvent(new PropertyEditorValueChangedEventArgs<(TElement, TElement, TElement, TElement)>(
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
                && TElement.TryParse(InnerThirdTextBox?.Text, CultureInfo.CurrentCulture, out _)
                && TElement.TryParse(InnerFourthTextBox?.Text, CultureInfo.CurrentCulture, out _))));
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
                FirstValue = SecondValue = ThirdValue = FourthValue = value;
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
                    case "PART_InnerFourthTextBox":
                        FourthValue = value;
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
    FocusAnyTextBox,
    FocusFirstTextBox, FocusSecondTextBox, FocusThirdTextBox, FocusFourthTextBox,
    BorderPointerOver, Uniform)]
[TemplatePart("PART_InnerFirstTextBox", typeof(TextBox))]
[TemplatePart("PART_InnerSecondTextBox", typeof(TextBox))]
[TemplatePart("PART_InnerThirdTextBox", typeof(TextBox))]
[TemplatePart("PART_InnerFourthTextBox", typeof(TextBox))]
[TemplatePart("PART_InnerUniformTextBox", typeof(TextBox))]
[TemplatePart("PART_BackgroundBorder", typeof(Border))]
public class Vector4Editor : PropertyEditor
{
    public static readonly DirectProperty<Vector4Editor, string> FirstTextProperty =
        AvaloniaProperty.RegisterDirect<Vector4Editor, string>(
            nameof(FirstText),
            o => o.FirstText,
            (o, v) => o.FirstText = v,
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly DirectProperty<Vector4Editor, string> SecondTextProperty =
        AvaloniaProperty.RegisterDirect<Vector4Editor, string>(
            nameof(SecondText),
            o => o.SecondText,
            (o, v) => o.SecondText = v,
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly DirectProperty<Vector4Editor, string> ThirdTextProperty =
        AvaloniaProperty.RegisterDirect<Vector4Editor, string>(
            nameof(ThirdText),
            o => o.ThirdText,
            (o, v) => o.ThirdText = v,
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly DirectProperty<Vector4Editor, string> FourthTextProperty =
        AvaloniaProperty.RegisterDirect<Vector4Editor, string>(
            nameof(FourthText),
            o => o.FourthText,
            (o, v) => o.FourthText = v,
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string?> FirstHeaderProperty =
        AvaloniaProperty.Register<Vector4Editor, string?>(nameof(FirstHeader));

    public static readonly StyledProperty<string?> SecondHeaderProperty =
        AvaloniaProperty.Register<Vector4Editor, string?>(nameof(SecondHeader));

    public static readonly StyledProperty<string?> ThirdHeaderProperty =
        AvaloniaProperty.Register<Vector4Editor, string?>(nameof(ThirdHeader));

    public static readonly StyledProperty<string?> FourthHeaderProperty =
        AvaloniaProperty.Register<Vector4Editor, string?>(nameof(FourthHeader));

    public static readonly StyledProperty<bool> IsUniformProperty =
        AvaloniaProperty.Register<Vector4Editor, bool>(nameof(IsUniform));

    public static readonly StyledProperty<string?> NumberFormatProperty =
        AvaloniaProperty.Register<Vector4Editor, string?>(
            nameof(NumberFormat),
            defaultValue: null);

    private const string FocusAnyTextBox = ":focus-any-textbox";
    private const string FocusFirstTextBox = ":focus-1st-textbox";
    private const string FocusSecondTextBox = ":focus-2nd-textbox";
    private const string FocusThirdTextBox = ":focus-3rd-textbox";
    private const string FocusFourthTextBox = ":focus-4th-textbox";
    private const string BorderPointerOver = ":border-pointerover";
    private const string Uniform = ":uniform";
    private readonly CompositeDisposable _disposables = [];
    private Border? _backgroundBorder;
    private string _firstText = string.Empty;
    private string _secondText = string.Empty;
    private string _thirdText = string.Empty;
    private string _fourthText = string.Empty;

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

    public string FourthText
    {
        get => _fourthText;
        set => SetAndRaise(FourthTextProperty, ref _fourthText, value);
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

    public string? FourthHeader
    {
        get => GetValue(FourthHeaderProperty);
        set => SetValue(FourthHeaderProperty, value);
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

    protected TextBox? InnerFourthTextBox { get; private set; }

    protected override Type StyleKeyOverride => typeof(Vector4Editor);

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        _disposables.Clear();
        base.OnApplyTemplate(e);
        InnerFirstTextBox = e.NameScope.Get<TextBox>("PART_InnerFirstTextBox");
        InnerSecondTextBox = e.NameScope.Find<TextBox>("PART_InnerSecondTextBox");
        InnerThirdTextBox = e.NameScope.Find<TextBox>("PART_InnerThirdTextBox");
        InnerFourthTextBox = e.NameScope.Find<TextBox>("PART_InnerFourthTextBox");
        _backgroundBorder = e.NameScope.Find<Border>("PART_BackgroundBorder");

        var hoverHandlers = new ComponentHoverHandlers(OnInnerTextBoxGotFocus, OnInnerTextBoxLostFocus, IsPointerOverChanged);
        hoverHandlers.SubscribeTextBox(InnerFirstTextBox, _disposables);
        hoverHandlers.SubscribeTextBox(InnerSecondTextBox, _disposables);
        hoverHandlers.SubscribeTextBox(InnerThirdTextBox, _disposables);
        hoverHandlers.SubscribeTextBox(InnerFourthTextBox, _disposables);

        hoverHandlers.SubscribePointerOver(_backgroundBorder, _disposables);

        UpdateFocusState();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsUniformProperty)
        {
            PseudoClasses.Set(Uniform, change.GetNewValue<bool>());
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        Size measured = base.MeasureOverride(availableSize);
        UpdateAutoCompact(availableSize);
        return measured;
    }

    private void IsPointerOverChanged(bool obj)
    {
        if (_backgroundBorder?.IsPointerOver == true
            || InnerFirstTextBox.IsPointerOver
            || InnerSecondTextBox?.IsPointerOver == true
            || InnerThirdTextBox?.IsPointerOver == true
            || InnerFourthTextBox?.IsPointerOver == true)
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
        PseudoClasses.Remove(FocusFourthTextBox);
        if (InnerFirstTextBox.IsFocused)
            PseudoClasses.Add(FocusFirstTextBox);
        else if (InnerSecondTextBox?.IsFocused == true)
            PseudoClasses.Add(FocusSecondTextBox);
        else if (InnerThirdTextBox?.IsFocused == true)
            PseudoClasses.Add(FocusThirdTextBox);
        else if (InnerFourthTextBox?.IsFocused == true)
            PseudoClasses.Add(FocusFourthTextBox);

        if (InnerFirstTextBox.IsFocused
            || InnerSecondTextBox?.IsFocused == true
            || InnerThirdTextBox?.IsFocused == true
            || InnerFourthTextBox?.IsFocused == true)
        {
            PseudoClasses.Add(FocusAnyTextBox);
        }
        else
        {
            PseudoClasses.Remove(FocusAnyTextBox);
        }
    }
}
