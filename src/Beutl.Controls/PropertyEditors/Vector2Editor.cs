using System.Globalization;
using System.Numerics;
using System.Reactive.Disposables;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;

using Beutl.Reactive;

namespace Beutl.Controls.PropertyEditors;

public class Vector2Editor<TElement> : Vector2Editor
    where TElement : INumber<TElement>
{
    public static readonly DirectProperty<Vector2Editor<TElement>, TElement> FirstValueProperty =
        Vector4Editor<TElement>.FirstValueProperty.AddOwner<Vector2Editor<TElement>>(
            o => o.FirstValue,
            (o, v) => o.FirstValue = v);

    public static readonly DirectProperty<Vector2Editor<TElement>, TElement> SecondValueProperty =
        Vector4Editor<TElement>.SecondValueProperty.AddOwner<Vector2Editor<TElement>>(
            o => o.SecondValue,
            (o, v) => o.SecondValue = v);

    public static readonly StyledProperty<TElement> LargeChangeProperty =
        Vector4Editor<TElement>.LargeChangeProperty.AddOwner<Vector2Editor<TElement>>();

    public static readonly StyledProperty<TElement> SmallChangeProperty =
        Vector4Editor<TElement>.SmallChangeProperty.AddOwner<Vector2Editor<TElement>>();

    private readonly CompositeDisposable _disposables = [];
    private TElement _firstValue = TElement.Zero;
    private TElement _oldFirstValue = TElement.Zero;
    private TElement _secondValue = TElement.Zero;
    private TElement _oldSecondValue = TElement.Zero;
    private TextBlock? _headerText;
    private HeaderScrubGesture _scrub;

    public Vector2Editor()
    {
        FirstHeader = "0";
        SecondHeader = "0";
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
        base.OnApplyTemplate(e);
        FirstText = NumberEditorHelper.Format(_firstValue, NumberFormat);
        SecondText = NumberEditorHelper.Format(_secondValue, NumberFormat);

        var valueHandlers = new ComponentValueHandlers(
            OnInnerTextBoxGotFocus, OnInnerTextBoxLostFocus, OnInnerTextBoxTextChanged, OnInnerTextBoxPointerWheelChanged);
        valueHandlers.Subscribe(InnerFirstTextBox, _disposables);
        valueHandlers.Subscribe(InnerSecondTextBox, _disposables);

        var headerHandlers = new ScrubHeaderHandlers(
            OnTextBlockPointerPressed, OnTextBlockPointerReleased, OnTextBlockPointerMoved, RoutingStrategies.Tunnel);
        headerHandlers.Subscribe(FirstHeaderTextBlock, _disposables);
        headerHandlers.Subscribe(SecondHeaderTextBlock, _disposables);
        _headerText = e.NameScope.Find<TextBlock>("PART_HeaderTextBlock");
        headerHandlers.Subscribe(_headerText, _disposables);

        UpdateErrors();
    }

    private void OnTextBlockPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!(InnerFirstTextBox.IsKeyboardFocusWithin || InnerSecondTextBox?.IsKeyboardFocusWithin == true)
            && _scrub.IsActive
            && sender is TextBlock headerText)
        {
            TElement delta = _scrub.NextDelta<TElement>(headerText, e) * SmallChange;

            var newValues = (FirstValue, SecondValue);
            var oldValues = (FirstValue, SecondValue);
            switch (headerText.Name)
            {
                case "PART_HeaderFirstTextBlock":
                    newValues.FirstValue = NumberEditorHelper.AddPreservingScale(newValues.FirstValue, delta);
                    break;
                case "PART_HeaderSecondTextBlock":
                    newValues.SecondValue = NumberEditorHelper.AddPreservingScale(newValues.SecondValue, delta);
                    break;
                case "PART_HeaderTextBlock":
                    newValues.FirstValue = NumberEditorHelper.AddPreservingScale(newValues.FirstValue, delta);
                    newValues.SecondValue = NumberEditorHelper.AddPreservingScale(newValues.SecondValue, delta);
                    break;
                default:
                    break;
            }

            (FirstValue, SecondValue) = newValues;
            RaiseEvent(new PropertyEditorValueChangedEventArgs<(TElement, TElement)>(
                newValues, oldValues, ValueChangedEvent));

            e.Handled = true;

            UpdateErrors();
        }
    }

    private void OnTextBlockPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_scrub.IsActive)
        {
            RaiseConfirmedIfChanged();

            _scrub.End();
            e.Handled = true;
        }
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
    }

    private void RaiseConfirmedIfChanged()
    {
        if (FirstValue != _oldFirstValue
            || SecondValue != _oldSecondValue)
        {
            RaiseEvent(new PropertyEditorValueChangedEventArgs<(TElement, TElement)>(
                (FirstValue, SecondValue),
                (_oldFirstValue, _oldSecondValue),
                ValueConfirmedEvent));
        }
    }

    private void OnInnerTextBoxTextChanged(TextBox sender, string? newValue, string? oldValue)
    {
        if (sender.IsKeyboardFocusWithin
            && NumberEditorHelper.TryParseEdit(newValue, oldValue, out TElement? newValue2, out TElement? oldValue2))
        {
            var newValues = (FirstValue, SecondValue);
            var oldValues = (FirstValue, SecondValue);
            if (IsUniform)
            {
                FirstValue = SecondValue = newValue2;
                newValues = (newValue2, newValue2);
                oldValues = (oldValue2, oldValue2);
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
                }
            }

            RaiseEvent(new PropertyEditorValueChangedEventArgs<(TElement, TElement)>(
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
                || TElement.TryParse(InnerSecondTextBox?.Text, CultureInfo.CurrentCulture, out _)));
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
                FirstValue = SecondValue = value;
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
                    default:
                        break;
                }
            }

            e.Handled = true;
        }
    }
}

[PseudoClasses(
    FocusAnyTextBox, FocusFirstTextBox, FocusSecondTextBox,
    BorderPointerOver, Uniform)]
[TemplatePart("PART_InnerFirstTextBox", typeof(TextBox))]
[TemplatePart("PART_InnerSecondTextBox", typeof(TextBox))]
[TemplatePart("PART_HeaderFirstTextBlock", typeof(TextBlock))]
[TemplatePart("PART_HeaderSecondTextBlock", typeof(TextBlock))]
[TemplatePart("PART_BackgroundBorder", typeof(Border))]
public class Vector2Editor : PropertyEditor
{
    public static readonly DirectProperty<Vector2Editor, string> FirstTextProperty =
        Vector4Editor.FirstTextProperty.AddOwner<Vector2Editor>(
            o => o.FirstText,
            (o, v) => o.FirstText = v);

    public static readonly DirectProperty<Vector2Editor, string> SecondTextProperty =
        Vector4Editor.SecondTextProperty.AddOwner<Vector2Editor>(
            o => o.SecondText,
            (o, v) => o.SecondText = v);

    public static readonly StyledProperty<string?> FirstHeaderProperty =
        Vector4Editor.FirstHeaderProperty.AddOwner<Vector2Editor>();

    public static readonly StyledProperty<string?> SecondHeaderProperty =
        Vector4Editor.SecondHeaderProperty.AddOwner<Vector2Editor>();

    public static readonly StyledProperty<bool> IsUniformProperty =
        Vector4Editor.IsUniformProperty.AddOwner<Vector2Editor>();

    public static readonly StyledProperty<string?> NumberFormatProperty =
        Vector4Editor.NumberFormatProperty.AddOwner<Vector2Editor>();

    private const string FocusAnyTextBox = ":focus-any-textbox";
    private const string FocusFirstTextBox = ":focus-1st-textbox";
    private const string FocusSecondTextBox = ":focus-2nd-textbox";
    private const string BorderPointerOver = ":border-pointerover";
    private const string Uniform = ":uniform";
    private readonly CompositeDisposable _disposables = [];
    private Border? _backgroundBorder;
    private string _firstText = string.Empty;
    private string _secondText = string.Empty;

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

    protected TextBlock? FirstHeaderTextBlock { get; private set; }

    protected TextBlock? SecondHeaderTextBlock { get; private set; }

    protected override Type StyleKeyOverride => typeof(Vector2Editor);

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        InnerFirstTextBox = e.NameScope.Get<TextBox>("PART_InnerFirstTextBox");
        InnerSecondTextBox = e.NameScope.Find<TextBox>("PART_InnerSecondTextBox");
        FirstHeaderTextBlock = e.NameScope.Find<TextBlock>("PART_HeaderFirstTextBlock");
        SecondHeaderTextBlock = e.NameScope.Find<TextBlock>("PART_HeaderSecondTextBlock");
        _backgroundBorder = e.NameScope.Find<Border>("PART_BackgroundBorder");

        var hoverHandlers = new ComponentHoverHandlers(OnInnerTextBoxGotFocus, OnInnerTextBoxLostFocus, IsPointerOverChanged);
        hoverHandlers.SubscribeTextBox(InnerFirstTextBox, _disposables);
        hoverHandlers.SubscribeTextBox(InnerSecondTextBox, _disposables);
        hoverHandlers.SubscribePointerOver(FirstHeaderTextBlock, _disposables);
        hoverHandlers.SubscribePointerOver(SecondHeaderTextBlock, _disposables);

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
            || FirstHeaderTextBlock?.IsPointerOver == true
            || SecondHeaderTextBlock?.IsPointerOver == true)
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
        if (InnerFirstTextBox.IsFocused)
            PseudoClasses.Add(FocusFirstTextBox);
        else if (InnerSecondTextBox?.IsFocused == true)
            PseudoClasses.Add(FocusSecondTextBox);

        if (InnerFirstTextBox.IsFocused
            || InnerSecondTextBox?.IsFocused == true)
        {
            PseudoClasses.Add(FocusAnyTextBox);
        }
        else
        {
            PseudoClasses.Remove(FocusAnyTextBox);
        }
    }
}
