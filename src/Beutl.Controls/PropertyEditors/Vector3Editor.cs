using System.Globalization;
using System.Numerics;

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

    private TElement _firstValue;
    private TElement _oldFirstValue;
    private TElement _secondValue;
    private TElement _oldSecondValue;
    private TElement _thirdValue;
    private TElement _oldThirdValue;

    private Point _headerDragStart;
    private bool _headerPressed;

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

        void SubscribeEvents2(TextBlock textBlock)
        {
            if (textBlock != null)
            {
                textBlock.AddHandler(PointerPressedEvent, OnTextBlockPointerPressed, RoutingStrategies.Tunnel);
                textBlock.AddHandler(PointerReleasedEvent, OnTextBlockPointerReleased, RoutingStrategies.Tunnel);
                textBlock.AddHandler(PointerMovedEvent, OnTextBlockPointerMoved, RoutingStrategies.Tunnel);
                textBlock.Cursor = CursorHelper.SizeWestEast;
            }
        }

        base.OnApplyTemplate(e);
        FirstText = _firstValue.ToString();
        SecondText = _secondValue.ToString();
        ThirdText = _thirdValue.ToString();

        SubscribeEvents(InnerFirstTextBox);
        SubscribeEvents(InnerSecondTextBox);
        SubscribeEvents(InnerThirdTextBox);

        SubscribeEvents2(FirstHeaderTextBlock);
        SubscribeEvents2(SecondHeaderTextBlock);
        SubscribeEvents2(ThirdHeaderTextBlock);
    }

    private void OnTextBlockPointerMoved(object sender, PointerEventArgs e)
    {
        if (!(InnerFirstTextBox.IsKeyboardFocusWithin || InnerSecondTextBox.IsKeyboardFocusWithin)
            && _headerPressed
            && sender is TextBlock headerText)
        {
            Point point = e.GetPosition(headerText);

            // 値を更新
            Point move = point - _headerDragStart;
            TElement delta = TElement.CreateTruncating(move.X);
            TElement oldValue;
            TElement newValue;

            var newValues = (FirstValue, SecondValue, ThirdValue);
            var oldValues = (FirstValue, SecondValue, ThirdValue);
            switch (headerText.Name)
            {
                case "PART_HeaderFirstTextBlock":
                    oldValue = FirstValue;
                    newValue = FirstValue + delta;
                    newValues.FirstValue = newValue;
                    oldValues.FirstValue = oldValue;
                    break;
                case "PART_HeaderSecondTextBlock":
                    oldValue = SecondValue;
                    newValue = SecondValue + delta;
                    newValues.SecondValue = newValue;
                    oldValues.SecondValue = oldValue;
                    break;
                case "PART_HeaderThirdTextBlock":
                    oldValue = ThirdValue;
                    newValue = ThirdValue + delta;
                    newValues.ThirdValue = newValue;
                    oldValues.ThirdValue = oldValue;
                    break;
                default:
                    break;
            }

            RaiseEvent(new PropertyEditorValueChangedEventArgs<(TElement, TElement, TElement)>(
                newValues, oldValues, ValueChangingEvent));

            _headerDragStart = point;

            // ポインターの位置が画面の端に付いた場合、位置を変更する
            CursorHelper.AdjustCursorPosition(headerText, point, ref _headerDragStart);

            e.Handled = true;

            UpdateErrors();
        }
    }

    private void OnTextBlockPointerReleased(object sender, PointerReleasedEventArgs e)
    {
        if (_headerPressed)
        {
            if (FirstValue != _oldFirstValue
                || SecondValue != _oldSecondValue
                || ThirdValue != _oldThirdValue)
            {
                RaiseEvent(new PropertyEditorValueChangedEventArgs<(TElement, TElement, TElement)>(
                    (FirstValue, SecondValue, ThirdValue),
                    (_oldFirstValue, _oldSecondValue, _oldThirdValue),
                    ValueChangedEvent));
            }

            _headerPressed = false;
            e.Handled = true;
        }
    }

    private void OnTextBlockPointerPressed(object sender, PointerPressedEventArgs e)
    {
        if (sender is TextBlock headerText)
        {
            PointerPoint pointerPoint = e.GetCurrentPoint(headerText);
            if (pointerPoint.Properties.IsLeftButtonPressed
                && !DataValidationErrors.GetHasErrors(this))
            {
                _oldFirstValue = FirstValue;
                _oldSecondValue = SecondValue;
                _oldThirdValue = ThirdValue;

                _headerDragStart = pointerPoint.Position;
                _headerPressed = true;
                e.Handled = true;
            }
        }
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
            if (FirstValue != _oldFirstValue
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
                var newValues = (FirstValue, SecondValue, ThirdValue);
                var oldValues = (FirstValue, SecondValue, ThirdValue);
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

                RaiseEvent(new PropertyEditorValueChangedEventArgs<(TElement, TElement, TElement)>(
                    newValues, oldValues, ValueChangingEvent));
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
            TElement delta = TElement.CreateTruncating(10);
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                delta = TElement.One;
            }

            value = e.Delta.Y switch
            {
                < 0 => value - delta,
                > 0 => value + delta,
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

    protected TextBlock FirstHeaderTextBlock { get; private set; }

    protected TextBlock SecondHeaderTextBlock { get; private set; }

    protected TextBlock ThirdHeaderTextBlock { get; private set; }

    protected override Type StyleKeyOverride => typeof(Vector3Editor);

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
        FirstHeaderTextBlock = e.NameScope.Find<TextBlock>("PART_HeaderFirstTextBlock");
        SecondHeaderTextBlock = e.NameScope.Find<TextBlock>("PART_HeaderSecondTextBlock");
        ThirdHeaderTextBlock = e.NameScope.Find<TextBlock>("PART_HeaderThirdTextBlock");
        _backgroundBorder = e.NameScope.Get<Border>("PART_BackgroundBorder");

        SubscribeEvents(InnerFirstTextBox);
        SubscribeEvents(InnerSecondTextBox);
        SubscribeEvents(InnerThirdTextBox);
        FirstHeaderTextBlock?.GetObservable(IsPointerOverProperty).Subscribe(IsPointerOverChanged);
        SecondHeaderTextBlock?.GetObservable(IsPointerOverProperty).Subscribe(IsPointerOverChanged);
        ThirdHeaderTextBlock?.GetObservable(IsPointerOverProperty).Subscribe(IsPointerOverChanged);

        _backgroundBorder.GetObservable(IsPointerOverProperty).Subscribe(IsPointerOverChanged);
    }

    private void IsPointerOverChanged(bool obj)
    {
        if (_backgroundBorder.IsPointerOver
            || InnerFirstTextBox.IsPointerOver
            || InnerSecondTextBox.IsPointerOver
            || InnerThirdTextBox.IsPointerOver
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
