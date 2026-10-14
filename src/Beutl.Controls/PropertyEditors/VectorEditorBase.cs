using System.Reactive.Disposables;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Beutl.Controls.PropertyEditors;

// The parts, pseudo-classes and text box handling of Vector2Editor, Vector3Editor and Vector4Editor, by component
// index. The derived editors set Values, which holds the values behind the text boxes.
public abstract class VectorEditorBase : PropertyEditor
{
    private static readonly string[] s_textBoxNames =
        ["PART_InnerFirstTextBox", "PART_InnerSecondTextBox", "PART_InnerThirdTextBox", "PART_InnerFourthTextBox"];

    private static readonly string[] s_headerNames =
        ["PART_HeaderFirstTextBlock", "PART_HeaderSecondTextBlock", "PART_HeaderThirdTextBlock"];

    private static readonly string[] s_focusPseudoClasses =
        [":focus-1st-textbox", ":focus-2nd-textbox", ":focus-3rd-textbox", ":focus-4th-textbox"];

    private const string FocusAnyTextBox = ":focus-any-textbox";
    private const string BorderPointerOver = ":border-pointerover";
    private const string Uniform = ":uniform";

    private readonly CompositeDisposable _disposables = [];
    private readonly DirectPropertyBase<string>[] _textProperties;
    private readonly string[] _texts;
    private readonly StyledProperty<bool> _isUniformProperty;
    private readonly TextBox?[] _textBoxes;
    private readonly TextBlock?[] _componentHeaders;
    private Border? _backgroundBorder;
    private HeaderScrubGesture _scrub;

    // With componentHeaders, each component has a header that scrubs that component alone; otherwise only the
    // editor's header scrubs, and it scrubs every component.
    private protected VectorEditorBase(
        DirectPropertyBase<string>[] textProperties, StyledProperty<bool> isUniformProperty, bool componentHeaders)
    {
        _textProperties = textProperties;
        _texts = new string[textProperties.Length];
        Array.Fill(_texts, string.Empty);
        _isUniformProperty = isUniformProperty;
        _textBoxes = new TextBox?[textProperties.Length];
        _componentHeaders = componentHeaders ? new TextBlock?[textProperties.Length] : [];
    }

    private protected VectorValues? Values { get; init; }

    internal bool IsUniformValue => GetValue(_isUniformProperty);

    internal string GetText(int index) => _texts[index];

    internal void SetText(int index, string value) => SetAndRaise(_textProperties[index], ref _texts[index], value);

    internal bool SetComponent<T>(DirectPropertyBase<T> property, ref T field, T value)
        => SetAndRaise(property, ref field, value);

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        _disposables.Clear();
        base.OnApplyTemplate(e);
        _textBoxes[0] = e.NameScope.Get<TextBox>(s_textBoxNames[0]);
        for (int i = 1; i < _textBoxes.Length; i++)
            _textBoxes[i] = e.NameScope.Find<TextBox>(s_textBoxNames[i]);

        for (int i = 0; i < _componentHeaders.Length; i++)
            _componentHeaders[i] = e.NameScope.Find<TextBlock>(s_headerNames[i]);

        _backgroundBorder = e.NameScope.Find<Border>("PART_BackgroundBorder");

        var hoverHandlers = new ComponentHoverHandlers(OnInnerTextBoxFocusChanged, OnInnerTextBoxFocusChanged, IsPointerOverChanged);
        foreach (TextBox? textBox in _textBoxes)
            hoverHandlers.SubscribeTextBox(textBox, _disposables);

        foreach (TextBlock? header in _componentHeaders)
            hoverHandlers.SubscribePointerOver(header, _disposables);

        hoverHandlers.SubscribePointerOver(_backgroundBorder, _disposables);

        if (Values is not { } values) return;

        values.FormatTexts();

        var valueHandlers = new ComponentValueHandlers(
            OnInnerTextBoxGotFocus, OnInnerTextBoxLostFocus, OnInnerTextBoxTextChanged, OnInnerTextBoxPointerWheelChanged);
        foreach (TextBox? textBox in _textBoxes)
            valueHandlers.Subscribe(textBox, _disposables);

        if (values.CanScrub)
        {
            var headerHandlers = new ScrubHeaderHandlers(
                OnHeaderPointerPressed, OnHeaderPointerReleased, OnHeaderPointerMoved, OnHeaderPointerCaptureLost,
                _componentHeaders.Length > 0 ? RoutingStrategies.Tunnel : RoutingStrategies.Direct | RoutingStrategies.Bubble);
            foreach (TextBlock? header in _componentHeaders)
                headerHandlers.Subscribe(header, _disposables);

            headerHandlers.Subscribe(e.NameScope.Find<TextBlock>("PART_HeaderTextBlock"), _disposables);
        }

        UpdateErrors();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == _isUniformProperty)
        {
            PseudoClasses.Set(Uniform, change.GetNewValue<bool>());
        }
    }

    private protected void UpdateFocusState()
    {
        for (int i = 0; i < _textBoxes.Length; i++)
            PseudoClasses.Remove(s_focusPseudoClasses[i]);

        int focused = Array.FindIndex(_textBoxes, textBox => textBox?.IsFocused == true);
        if (focused >= 0)
            PseudoClasses.Add(s_focusPseudoClasses[focused]);

        PseudoClasses.Set(FocusAnyTextBox, focused >= 0);
    }

    private void IsPointerOverChanged(bool obj)
    {
        bool isPointerOver = _backgroundBorder?.IsPointerOver == true
            || Array.Exists(_textBoxes, textBox => textBox?.IsPointerOver == true)
            || Array.Exists(_componentHeaders, header => header?.IsPointerOver == true);
        PseudoClasses.Set(BorderPointerOver, isPointerOver);
    }

    private void OnInnerTextBoxFocusChanged(object? sender, RoutedEventArgs e)
    {
        UpdateFocusState();
    }

    private void OnInnerTextBoxGotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (!DataValidationErrors.GetHasErrors(this))
        {
            Values!.Snapshot();
        }
    }

    private void OnInnerTextBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        if (!DataValidationErrors.GetHasErrors(this))
        {
            Values!.RaiseConfirmedIfChanged();
        }
    }

    private void OnInnerTextBoxTextChanged(TextBox sender, string? newValue, string? oldValue)
    {
        if (sender.IsKeyboardFocusWithin)
        {
            Values!.Edit(Array.IndexOf(_textBoxes, sender), newValue, oldValue);
        }

        UpdateErrors();
    }

    private void UpdateErrors()
    {
        VectorValues values = Values!;
        bool valid = values.IsValid(_textBoxes[0]!.Text);
        if (valid && !IsUniformValue)
        {
            for (int i = 1; i < _textBoxes.Length && valid; i++)
                valid = values.IsValid(_textBoxes[i]?.Text);
        }

        DataValidationMessages.UpdateInvalidString(this, valid);
    }

    private void OnInnerTextBoxPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (!DataValidationErrors.GetHasErrors(this)
            && sender is TextBox textBox
            && textBox.IsKeyboardFocusWithin
            && Values!.Step(Array.IndexOf(_textBoxes, textBox), textBox.Text, e))
        {
            e.Handled = true;
        }
    }

    private void OnHeaderPointerMoved(object? sender, PointerEventArgs e)
    {
        // A move with the button up means the release never reached the header.
        if (_scrub.IsActive && !e.Properties.IsLeftButtonPressed)
        {
            CompleteScrub();
            return;
        }

        if (!Array.Exists(_textBoxes, textBox => textBox?.IsKeyboardFocusWithin == true)
            && _scrub.IsActive
            && sender is TextBlock header)
        {
            Values!.Scrub(Array.IndexOf(_componentHeaders, header), ref _scrub, header, e);

            e.Handled = true;

            UpdateErrors();
        }
    }

    private void OnHeaderPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_scrub.IsActive)
        {
            CompleteScrub();
            e.Handled = true;
        }
    }

    // Another window, a dialog or a rebuilt header took the pointer; end as a release would.
    private void OnHeaderPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        CompleteScrub();
    }

    private void CompleteScrub()
    {
        if (!_scrub.IsActive) return;

        // Ended before confirming, so a confirmation that rebuilds the header cannot end it twice.
        _scrub.End();
        Values!.RaiseConfirmedIfChanged();
    }

    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is TextBlock header)
        {
            PointerPoint pointerPoint = e.GetCurrentPoint(header);
            if (pointerPoint.Properties.IsLeftButtonPressed
                && !DataValidationErrors.GetHasErrors(this))
            {
                Values!.Snapshot();
                _scrub.Begin(header, pointerPoint.Position);
                e.Handled = true;
            }
        }
    }
}
