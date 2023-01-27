using System.Globalization;
using System.Numerics;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;

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

    private TElement _firstValue;
    private TElement _oldFirstValue;
    private TElement _secondValue;
    private TElement _oldSecondValue;
    private TElement _thirdValue;
    private TElement _oldThirdValue;

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

        SubscribeEvents(InnerFirstTextBox);
        SubscribeEvents(InnerSecondTextBox);
        SubscribeEvents(InnerThirdTextBox);
    }

    private void OnInnerTextBoxGotFocus(object sender, GotFocusEventArgs e)
    {
        if (!DataValidationErrors.GetHasErrors(this))
        {
            _oldFirstValue = FirstValue;
            _oldSecondValue = SecondValue;
            _oldThirdValue = ThirdValue;
        }
    }

    private void OnInnerTextBoxLostFocus(object sender, RoutedEventArgs e)
    {
        if (!DataValidationErrors.GetHasErrors(this))
        {
            if (
                FirstValue != _oldFirstValue
                || SecondValue != _oldSecondValue
                || ThirdValue != _oldThirdValue)
            {
                RaiseEvent(new PropertyEditorValueChangedEventArgs<(TElement, TElement, TElement)>(
                    (FirstValue, SecondValue, ThirdValue),
                    (_oldFirstValue, _oldSecondValue, _oldThirdValue),
                    ValueChangedEvent));
            }
        }
    }

    private void OnInnerTextBoxTextChanged(TextBox sender, string newValue, string oldValue)
    {
        if (TElement.TryParse(newValue, CultureInfo.CurrentUICulture, out TElement newValue2))
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
                        RaiseEvent(new PropertyEditorValueChangedEventArgs<(TElement, TElement, TElement)>(
                            (newValue2, SecondValue, ThirdValue),
                            (oldValue2, SecondValue, ThirdValue),
                            ValueChangingEvent));
                        break;
                    case "PART_InnerSecondTextBox":
                        SecondValue = newValue2;
                        RaiseEvent(new PropertyEditorValueChangedEventArgs<(TElement, TElement, TElement)>(
                            (FirstValue, newValue2, ThirdValue),
                            (FirstValue, oldValue2, ThirdValue),
                            ValueChangingEvent));
                        break;
                    case "PART_InnerThirdTextBox":
                        ThirdValue = newValue2;
                        RaiseEvent(new PropertyEditorValueChangedEventArgs<(TElement, TElement, TElement)>(
                            (FirstValue, SecondValue, newValue2),
                            (FirstValue, SecondValue, oldValue2),
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
            && TElement.TryParse(InnerThirdTextBox.Text, CultureInfo.CurrentUICulture, out _))
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
[TemplatePart("PART_BackgroundBorder", typeof(Border))]
public class Vector3Editor : PropertyEditor, IStyleable
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

    public static readonly StyledProperty<string> FirstHeaderProperty =
        Vector4Editor.FirstHeaderProperty.AddOwner<Vector3Editor>();

    public static readonly StyledProperty<string> SecondHeaderProperty =
        Vector4Editor.SecondHeaderProperty.AddOwner<Vector3Editor>();

    public static readonly StyledProperty<string> ThirdHeaderProperty =
        Vector4Editor.ThirdHeaderProperty.AddOwner<Vector3Editor>();

    private const string FocusAnyTextBox = ":focus-any-textbox";
    private const string FocusFirstTextBox = ":focus-1st-textbox";
    private const string FocusSecondTextBox = ":focus-2nd-textbox";
    private const string FocusThirdTextBox = ":focus-3rd-textbox";
    private const string BorderPointerOver = ":border-pointerover";
    private Border _backgroundBorder;
    private string _firstText;
    private string _secondText;
    private string _thirdText;

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

    protected TextBox InnerFirstTextBox { get; private set; }

    protected TextBox InnerSecondTextBox { get; private set; }

    protected TextBox InnerThirdTextBox { get; private set; }

    Type IStyleable.StyleKey => typeof(Vector3Editor);

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
        _backgroundBorder = e.NameScope.Get<Border>("PART_BackgroundBorder");

        SubscribeEvents(InnerFirstTextBox);
        SubscribeEvents(InnerSecondTextBox);
        SubscribeEvents(InnerThirdTextBox);

        _backgroundBorder.GetObservable(IsPointerOverProperty).Subscribe(IsPointerOverChanged);
    }

    private void IsPointerOverChanged(bool obj)
    {
        if (_backgroundBorder.IsPointerOver
            || InnerFirstTextBox.IsPointerOver
            || InnerSecondTextBox.IsPointerOver
            || InnerThirdTextBox.IsPointerOver)
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
        if (InnerFirstTextBox.IsFocused)
            PseudoClasses.Add(FocusFirstTextBox);
        else if (InnerSecondTextBox.IsFocused)
            PseudoClasses.Add(FocusSecondTextBox);
        else if (InnerThirdTextBox.IsFocused)
            PseudoClasses.Add(FocusThirdTextBox);

        if (
            InnerFirstTextBox.IsFocused
            || InnerSecondTextBox.IsFocused
            || InnerThirdTextBox.IsFocused)
        {
            PseudoClasses.Add(FocusAnyTextBox);
        }
        else
        {
            PseudoClasses.Remove(FocusAnyTextBox);
        }
    }
}
