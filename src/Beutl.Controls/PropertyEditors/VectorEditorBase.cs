using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Logging;

using Beutl.Extensibility;
using Beutl.Reactive;

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
    private const string ScrubBox = "scrub-box";

    private readonly CompositeDisposable _disposables = [];
    private readonly DirectPropertyBase<string>[] _textProperties;
    private readonly string[] _texts;
    private readonly StyledProperty<bool> _isUniformProperty;
    private readonly TextBox?[] _textBoxes;
    private readonly TextBlock?[] _componentHeaders;
    private Border? _backgroundBorder;
    private readonly NumberBoxDrag _boxDrag;
    // The scrub of a header.
    private HeaderScrubGesture _scrub;
    private Control? _editSource;
    private (RoutedEventArgs Args, int EditedComponents, bool IsUnderlyingEdit)? _valueChange;

    private bool IsRelativeEdit => _editSource is not TextBox;

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
        _boxDrag = new NumberBoxDrag(
            this,
            began: () => Values!.Snapshot(),
            scrub: ScrubBoxComponent,
            ended: () => Values!.RaiseConfirmedIfChanged(),
            clicked: box =>
            {
                if (box is TextBox textBox && textBox.Focus())
                    textBox.SelectAll();
            },
            // A press on a box being edited, or on a value that does not parse, goes to the text box.
            canArm: box => !box.IsKeyboardFocusWithin && !DataValidationErrors.GetHasErrors(this),
            left: () => IsPointerOverChanged(false));
    }

    private protected VectorValues? Values { get; init; }

    internal bool IsUniformValue => GetValue(_isUniformProperty);

    internal ITuple? HapticReferenceValues { get; set; }

    internal string GetText(int index) => _texts[index];

    internal void SetText(int index, string value)
    {
        if (_editSource == null || _editSource != _textBoxes[index])
        {
            // The text is the editor's own unless an edit already holds the source.
            using var edit = new PropertyEditorEditScope(ref _editSource, _editSource ?? this);
            SetAndRaise(_textProperties[index], ref _texts[index], value);
        }
    }

    internal bool IsTextBoxFocused(int index)
        => index >= 0 && index < _textBoxes.Length && _textBoxes[index]?.IsFocused == true;

    internal void RaiseValueChanged(RoutedEventArgs args, int editedComponents, bool isUnderlyingEdit = false)
    {
        var previous = _valueChange;
        _valueChange = (args, editedComponents, isUnderlyingEdit);
        try
        {
            RaiseEvent(args);
        }
        finally
        {
            _valueChange = previous;
        }
    }

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
        {
            if (textBox == null) continue;
            valueHandlers.Subscribe(textBox, _disposables);
            textBox.AddDisposableHandler(TextInputEvent, OnInnerTextBoxTextInput, RoutingStrategies.Tunnel)
                .DisposeWith(_disposables);
            textBox.AddDisposableHandler(TextBox.PastingFromClipboardEvent, OnInnerTextBoxPasting)
                .DisposeWith(_disposables);
        }

        if (values.CanScrub)
        {
            var headerHandlers = new ScrubHeaderHandlers(
                OnHeaderPointerPressed, OnHeaderPointerReleased, OnHeaderPointerMoved, OnHeaderPointerCaptureLost,
                _componentHeaders.Length > 0 ? RoutingStrategies.Tunnel : RoutingStrategies.Direct | RoutingStrategies.Bubble);
            foreach (TextBlock? header in _componentHeaders)
                headerHandlers.Subscribe(header, _disposables);

            headerHandlers.Subscribe(e.NameScope.Find<TextBlock>("PART_HeaderTextBlock"), _disposables);

            if (EditorStyle == PropertyEditorStyle.Normal)
                SubscribeBoxes();
        }

        UpdateErrors();
    }

    // A drag on a box scrubs its component as a drag of its header does, and a click edits its text. The press is taken
    // on its way to the text box, so that the many layouts of these editors need only the cursor of their boxes' theme.
    private void SubscribeBoxes()
    {
        foreach (TextBox? box in _textBoxes)
        {
            if (box == null) continue;

            _boxDrag.Subscribe(box, _disposables, RoutingStrategies.Tunnel);
            box.Classes.Add(ScrubBox);
            TextBoxAttachment.SetEnterDownBehavior(box, TextBoxAttachment.EnterBehaviorMode.LostFocus);
        }
    }

    // bind binds the boxes; show puts a preview's result in them and, after a confirmation, the value the context kept.
    private protected void ConnectComponents<TEdit>(
        IValueEditorContext<TEdit> context, Action<CompositeDisposable> bind, Action<TEdit> show,
        bool reportsMismatch = true)
    {
        CompositeDisposable connection = StartConnection();
        TEdit shown = default!;
        context.EditValue.Subscribe(value => shown = value).DisposeWith(connection);
        bind(connection);
        this.AddDisposableHandler(ValueChangedEvent, (s, e) =>
            {
                if ((reportsMismatch ? e.Expect<TEdit>(s) : e as PropertyEditorValueChangedEventArgs<TEdit>) is { } args)
                {
                    if (_valueChange is { } change && ReferenceEquals(change.Args, e))
                    {
                        show(change.IsUnderlyingEdit
                            ? PreviewFocusedEdit(context, args.NewValue)
                            : PreviewEdit(context, args.NewValue, IsRelativeEdit, change.EditedComponents));
                    }
                    else
                    {
                        show(PreviewEdit(context, args.NewValue, IsRelativeEdit));
                    }
                }
            })
            .DisposeWith(connection);
        ConfirmEdits(context, connection, () => show(shown));
    }

    // Shows one component of the context's value in property.
    private protected void BindComponent<T>(AvaloniaProperty<T> property, IObservable<T> values, CompositeDisposable connection)
    {
        Bind(property, values.DistinctUntilChanged().ToBinding()).DisposeWith(connection);
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
        // A pressed box holds the pointer, which leaves it and all the rest not over anything until the release.
        bool isPointerOver = _scrub.IsActive
            || _boxDrag.IsHeld
            || _backgroundBorder?.IsPointerOver == true
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
            int index = Array.IndexOf(_textBoxes, sender as TextBox);
            if (index >= 0)
            {
                using var edit = new PropertyEditorEditScope(ref _editSource, this);
                Values.FormatText(index, focused: true);
            }
        }
    }

    private void OnInnerTextBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        if (!DataValidationErrors.GetHasErrors(this))
        {
            int index = Array.IndexOf(_textBoxes, sender as TextBox);
            using var edit = new PropertyEditorEditScope(ref _editSource, this);
            Values!.RaiseConfirmedIfChanged();
            if (index >= 0)
            {
                Values.FormatText(index, focused: false);
            }
            else
            {
                Values.FormatTexts();
            }
        }
    }

    private void OnInnerTextBoxTextChanged(TextBox sender, string? newValue, string? oldValue)
    {
        if (_editSource == null && sender.IsKeyboardFocusWithin)
        {
            using var edit = new PropertyEditorEditScope(ref _editSource, sender);
            Values!.Edit(Array.IndexOf(_textBoxes, sender), newValue, oldValue);
        }

        UpdateErrors();
    }

    // Replacing a selection with identical text raises no text change, but can still change the linked components.
    private void OnInnerTextBoxTextInput(object? sender, TextInputEventArgs e)
    {
        if (IsUniformValue && !IsReadOnly && sender is TextBox { IsReadOnly: false } box
            && e.Text is { Length: > 0 } input && input == box.SelectedText)
        {
            OnInnerTextBoxTextChanged(box, box.Text, box.Text);
        }
    }

    private async void OnInnerTextBoxPasting(object? sender, RoutedEventArgs e)
    {
        if (!IsUniformValue || IsReadOnly || sender is not TextBox { IsReadOnly: false, IsKeyboardFocusWithin: true } box
            || box.SelectionStart == box.SelectionEnd || TopLevel.GetTopLevel(box)?.Clipboard is not { } clipboard)
        {
            return;
        }

        e.Handled = true;
        string? input;
        try
        {
            input = await clipboard.TryGetTextAsync();
        }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException or UnauthorizedAccessException or COMException)
        {
            Logger.TryGet(LogEventLevel.Warning, LogArea.Control)?.Log(this, "Failed to read text from clipboard: {Error}", ex);
            return;
        }

        if (string.IsNullOrEmpty(input) || !box.IsKeyboardFocusWithin || !Array.Exists(_textBoxes, part => ReferenceEquals(part, box)))
            return;

        string? oldText = box.Text;
        bool hadSelection = box.SelectionStart != box.SelectionEnd;
        box.SelectedText = input;
        if (IsUniformValue && hadSelection && box.SelectionStart == box.SelectionEnd && box.Text == oldText)
            OnInnerTextBoxTextChanged(box, box.Text, oldText);
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
            && textBox.IsKeyboardFocusWithin)
        {
            using var edit = new PropertyEditorEditScope(ref _editSource, this);
            if (Values!.Step(Array.IndexOf(_textBoxes, textBox), textBox.Text, e))
            {
                Values.FormatTexts();
                e.Handled = true;
            }
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

    // Scrubs the component of box, or every component while they are one value.
    private void ScrubBoxComponent(ref HeaderScrubGesture gesture, Control box, PointerEventArgs e)
    {
        Values!.Scrub(IsUniformValue ? -1 : Array.IndexOf(_textBoxes, box as TextBox), ref gesture, box, e);
        UpdateErrors();
    }

    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is TextBlock header)
        {
            PointerPoint pointerPoint = e.GetCurrentPoint(header);
            if (pointerPoint.Properties.IsLeftButtonPressed
                && !IsReadOnly
                && !DataValidationErrors.GetHasErrors(this))
            {
                Values!.Snapshot();
                _scrub.Begin(header, pointerPoint.Position);
                e.Handled = true;
            }
        }
    }
}
