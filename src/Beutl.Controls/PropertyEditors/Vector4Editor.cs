using System.Globalization;
using System.Numerics;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;

namespace Beutl.Controls.PropertyEditors;

public class Vector4Editor<TElement> : Vector4Editor
    where TElement : INumber<TElement>
{
#pragma warning disable AVP1002 // AvaloniaProperty objects should not be owned by a generic type
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
#pragma warning restore AVP1002 // AvaloniaProperty objects should not be owned by a generic type

    private TElement _firstValue;
    private TElement _oldFirstValue;
    private TElement _secondValue;
    private TElement _oldSecondValue;
    private TElement _thirdValue;
    private TElement _oldThirdValue;
    private TElement _fourthValue;
    private TElement _oldFourthValue;

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
                FirstText = value.ToString();
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
                SecondText = value.ToString();
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
                ThirdText = value.ToString();
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
                FourthText = value.ToString();
            }
        }
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        void SubscribeEvents(TextBox textBox)
        {
            textBox.GotFocus += OnInnerTextBoxGotFocus;
            textBox.LostFocus += OnInnerTextBoxLostFocus;
            textBox.GetPropertyChangedObservable(TextBox.TextProperty).Subscribe(e =>
            {
                if (e is AvaloniaPropertyChangedEventArgs<string> args
                    && args.Sender is TextBox textBox)
                {
                    OnInnerTextBoxTextChanged(textBox, args.NewValue.GetValueOrDefault(), args.OldValue.GetValueOrDefault());
                }
            });
            textBox.AddHandler(PointerWheelChangedEvent, OnInnerTextBoxPointerWheelChanged, RoutingStrategies.Tunnel);
        }

        base.OnApplyTemplate(e);
        FirstText = _firstValue.ToString();
        SecondText = _secondValue.ToString();
        ThirdText = _thirdValue.ToString();
        FourthText = _fourthValue.ToString();

        SubscribeEvents(InnerFirstTextBox);
        SubscribeEvents(InnerSecondTextBox);
        SubscribeEvents(InnerThirdTextBox);
        SubscribeEvents(InnerFourthTextBox);
    }

    private void OnInnerTextBoxGotFocus(object sender, GotFocusEventArgs e)
    {
        if (!DataValidationErrors.GetHasErrors(this))
        {
            _oldFirstValue = FirstValue;
            _oldSecondValue = SecondValue;
            _oldThirdValue = ThirdValue;
            _oldFourthValue = FourthValue;
        }
    }

    private void OnInnerTextBoxLostFocus(object sender, RoutedEventArgs e)
    {
        if (!DataValidationErrors.GetHasErrors(this))
        {
            if (
                FirstValue != _oldFirstValue
                || SecondValue != _oldSecondValue
                || ThirdValue != _oldThirdValue
                || FourthValue != _oldFourthValue)
            {
                RaiseEvent(new PropertyEditorValueChangedEventArgs<(TElement, TElement, TElement, TElement)>(
                    (FirstValue, SecondValue, ThirdValue, FourthValue),
                    (_oldFirstValue, _oldSecondValue, _oldThirdValue, _oldFourthValue),
                    ValueChangedEvent));
            }
        }
    }

    private void OnInnerTextBoxTextChanged(TextBox sender, string newValue, string oldValue)
    {
        if (sender.IsKeyboardFocusWithin
            && TElement.TryParse(newValue, CultureInfo.CurrentUICulture, out TElement newValue2))
        {
            bool invalidOldValue = !TElement.TryParse(oldValue, CultureInfo.CurrentUICulture, out TElement oldValue2);
            if (invalidOldValue)
            {
                oldValue2 = newValue2;
            }

            if (invalidOldValue || newValue2 != oldValue2)
            {
                switch (sender.Name)
                {
                    case "PART_InnerFirstTextBox":
                        FirstValue = newValue2;
                        RaiseEvent(new PropertyEditorValueChangedEventArgs<(TElement, TElement, TElement, TElement)>(
                            (newValue2, SecondValue, ThirdValue, FourthValue),
                            (oldValue2, SecondValue, ThirdValue, FourthValue),
                            ValueChangingEvent));
                        break;
                    case "PART_InnerSecondTextBox":
                        SecondValue = newValue2;
                        RaiseEvent(new PropertyEditorValueChangedEventArgs<(TElement, TElement, TElement, TElement)>(
                            (FirstValue, newValue2, ThirdValue, FourthValue),
                            (FirstValue, oldValue2, ThirdValue, FourthValue),
                            ValueChangingEvent));
                        break;
                    case "PART_InnerThirdTextBox":
                        ThirdValue = newValue2;
                        RaiseEvent(new PropertyEditorValueChangedEventArgs<(TElement, TElement, TElement, TElement)>(
                            (FirstValue, SecondValue, newValue2, FourthValue),
                            (FirstValue, SecondValue, oldValue2, FourthValue),
                            ValueChangingEvent));
                        break;
                    case "PART_InnerFourthTextBox":
                        FourthValue = newValue2;
                        RaiseEvent(new PropertyEditorValueChangedEventArgs<(TElement, TElement, TElement, TElement)>(
                            (FirstValue, SecondValue, ThirdValue, newValue2),
                            (FirstValue, SecondValue, ThirdValue, oldValue2),
                            ValueChangingEvent));
                        break;
                    default:
                        break;
                }
            }
        }

        UpdateErrors();
    }

    private void UpdateErrors()
    {
        if (
            TElement.TryParse(InnerFirstTextBox.Text, CultureInfo.CurrentUICulture, out _)
            && TElement.TryParse(InnerSecondTextBox.Text, CultureInfo.CurrentUICulture, out _)
            && TElement.TryParse(InnerThirdTextBox.Text, CultureInfo.CurrentUICulture, out _)
            && TElement.TryParse(InnerFourthTextBox.Text, CultureInfo.CurrentUICulture, out _))
        {
            DataValidationErrors.ClearErrors(this);
        }
        else
        {
            DataValidationErrors.SetErrors(this, DataValidationMessages.InvalidString);
        }
    }

    private void OnInnerTextBoxPointerWheelChanged(object sender, PointerWheelEventArgs e)
    {
        if (!DataValidationErrors.GetHasErrors(this)
            && sender is TextBox textBox
            && textBox.IsKeyboardFocusWithin
            && TElement.TryParse(textBox.Text, CultureInfo.CurrentUICulture, out TElement value))
        {
            value = e.Delta.Y switch
            {
                < 0 => value - TElement.CreateTruncating(10),
                > 0 => value + TElement.CreateTruncating(10),
                _ => value
            };

            value = e.Delta.X switch
            {
                < 0 => value - TElement.One,
                > 0 => value + TElement.One,
                _ => value
            };


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

            e.Handled = true;
        }
    }
}

[PseudoClasses(FocusAnyTextBox, BorderPointerOver)]
[TemplatePart("PART_InnerFirstTextBox", typeof(TextBox))]
[TemplatePart("PART_InnerSecondTextBox", typeof(TextBox))]
[TemplatePart("PART_InnerThirdTextBox", typeof(TextBox))]
[TemplatePart("PART_InnerFourthTextBox", typeof(TextBox))]
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

    public static readonly StyledProperty<string> FirstHeaderProperty =
        AvaloniaProperty.Register<Vector4Editor, string>(nameof(FirstHeader));

    public static readonly StyledProperty<string> SecondHeaderProperty =
        AvaloniaProperty.Register<Vector4Editor, string>(nameof(SecondHeader));

    public static readonly StyledProperty<string> ThirdHeaderProperty =
        AvaloniaProperty.Register<Vector4Editor, string>(nameof(ThirdHeader));

    public static readonly StyledProperty<string> FourthHeaderProperty =
        AvaloniaProperty.Register<Vector4Editor, string>(nameof(FourthHeader));

    private const string FocusAnyTextBox = ":focus-any-textbox";
    private const string FocusFirstTextBox = ":focus-1st-textbox";
    private const string FocusSecondTextBox = ":focus-2nd-textbox";
    private const string FocusThirdTextBox = ":focus-3rd-textbox";
    private const string FocusFourthTextBox = ":focus-4th-textbox";
    private const string BorderPointerOver = ":border-pointerover";
    private Border _backgroundBorder;
    private string _firstText;
    private string _secondText;
    private string _thirdText;
    private string _fourthText;

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

    public string FirstHeader
    {
        get => GetValue(FirstHeaderProperty);
        set => SetValue(FirstHeaderProperty, value);
    }

    public string SecondHeader
    {
        get => GetValue(SecondHeaderProperty);
        set => SetValue(SecondHeaderProperty, value);
    }

    public string ThirdHeader
    {
        get => GetValue(ThirdHeaderProperty);
        set => SetValue(ThirdHeaderProperty, value);
    }

    public string FourthHeader
    {
        get => GetValue(FourthHeaderProperty);
        set => SetValue(FourthHeaderProperty, value);
    }

    protected TextBox InnerFirstTextBox { get; private set; }

    protected TextBox InnerSecondTextBox { get; private set; }

    protected TextBox InnerThirdTextBox { get; private set; }

    protected TextBox InnerFourthTextBox { get; private set; }

    protected override Type StyleKeyOverride => typeof(Vector4Editor);

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        void SubscribeEvents(TextBox textBox)
        {
            textBox.GotFocus += OnInnerTextBoxGotFocus;
            textBox.LostFocus += OnInnerTextBoxLostFocus;
            textBox.GetObservable(IsPointerOverProperty).Subscribe(IsPointerOverChanged);
        }

        base.OnApplyTemplate(e);
        InnerFirstTextBox = e.NameScope.Get<TextBox>("PART_InnerFirstTextBox");
        InnerSecondTextBox = e.NameScope.Get<TextBox>("PART_InnerSecondTextBox");
        InnerThirdTextBox = e.NameScope.Get<TextBox>("PART_InnerThirdTextBox");
        InnerFourthTextBox = e.NameScope.Get<TextBox>("PART_InnerFourthTextBox");
        _backgroundBorder = e.NameScope.Get<Border>("PART_BackgroundBorder");

        SubscribeEvents(InnerFirstTextBox);
        SubscribeEvents(InnerSecondTextBox);
        SubscribeEvents(InnerThirdTextBox);
        SubscribeEvents(InnerFourthTextBox);

        _backgroundBorder.GetObservable(IsPointerOverProperty).Subscribe(IsPointerOverChanged);
    }

    private void IsPointerOverChanged(bool obj)
    {
        if (_backgroundBorder.IsPointerOver
            || InnerFirstTextBox.IsPointerOver
            || InnerSecondTextBox.IsPointerOver
            || InnerThirdTextBox.IsPointerOver
            || InnerFourthTextBox.IsPointerOver)
        {
            PseudoClasses.Add(BorderPointerOver);
        }
        else
        {
            PseudoClasses.Remove(BorderPointerOver);
        }
    }

    private void OnInnerTextBoxGotFocus(object sender, GotFocusEventArgs e)
    {
        UpdateFocusState();
    }

    private void OnInnerTextBoxLostFocus(object sender, RoutedEventArgs e)
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
        else if (InnerSecondTextBox.IsFocused)
            PseudoClasses.Add(FocusSecondTextBox);
        else if (InnerThirdTextBox.IsFocused)
            PseudoClasses.Add(FocusThirdTextBox);
        else if (InnerFourthTextBox.IsFocused)
            PseudoClasses.Add(FocusFourthTextBox);

        if (
            InnerFirstTextBox.IsFocused
            || InnerSecondTextBox.IsFocused
            || InnerThirdTextBox.IsFocused
            || InnerFourthTextBox.IsFocused)
        {
            PseudoClasses.Add(FocusAnyTextBox);
        }
        else
        {
            PseudoClasses.Remove(FocusAnyTextBox);
        }
    }
}
