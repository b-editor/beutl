using System.Reactive.Disposables;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;

using Beutl.Extensibility;
using Beutl.Reactive;

namespace Beutl.Controls.PropertyEditors;

public enum PropertyEditorStyle
{
    Normal,
    Compact,
    ListItem,
    Settings,
}

internal interface IPropertyEditorDragSession
{
    IDisposable Enter();

    void Confirm();
}

[PseudoClasses(":compact", ":list-item", ":settings", ":visible-left-button", ":visible-right-button")]
[TemplatePart("PART_LeftButton", typeof(Button))]
[TemplatePart("PART_RightButton", typeof(Button))]
[TemplatePart("PART_DeleteButton", typeof(Button))]
[TemplatePart("PART_ReorderHandle", typeof(Control))]
public partial class PropertyEditor : TemplatedControl, IPropertyEditorContextVisitor, IListItemEditor
{
    public static readonly DirectProperty<PropertyEditor, string?> HeaderTooltipProperty =
        AvaloniaProperty.RegisterDirect<PropertyEditor, string?>(nameof(HeaderTooltip), o => o.HeaderTooltip);

    public static readonly StyledProperty<int> KeyFrameCountProperty =
        AvaloniaProperty.Register<PropertyEditor, int>(nameof(KeyFrameCount), 0, coerce: (_, v) => Math.Max(v, 0));

    public static readonly StyledProperty<Control?> ReorderHandleProperty =
        AvaloniaProperty.Register<PropertyEditor, Control?>(nameof(ReorderHandle), null);

    public static readonly RoutedEvent<PropertyEditorValueChangedEventArgs> ValueChangedEvent =
        RoutedEvent.Register<PropertyEditor, PropertyEditorValueChangedEventArgs>(nameof(ValueChanged), RoutingStrategies.Bubble);

    public static readonly RoutedEvent<PropertyEditorValueChangedEventArgs> ValueConfirmedEvent =
        RoutedEvent.Register<PropertyEditor, PropertyEditorValueChangedEventArgs>(nameof(ValueConfirmed), RoutingStrategies.Bubble);

    private const double AutoCompactMaxWidth = 224;
    private readonly CompositeDisposable _eventRevokers = new(3);
    private readonly SerialDisposable _connection = new();
    private (Control Owner, IPointer Pointer, Action Stop)? _valueDrag;
    private IPropertyEditorDragSession? _dragSession;
    private string? _headerTooltip;

    static PropertyEditor()
    {
        MarginProperty.OverrideDefaultValue<PropertyEditor>(new(4, 0));
    }

    [GeneratedStyledProperty]
    public partial string? Header { get; set; }

    [GeneratedStyledProperty]
    public partial string? Description { get; set; }

    [GeneratedStyledProperty]
    public partial string? HoverInfo { get; set; }

    public string? HeaderTooltip => _headerTooltip;

    [GeneratedStyledProperty(AddOwnerFrom = typeof(TextBox))]
    public partial bool IsReadOnly { get; set; }

    [GeneratedStyledProperty(DefaultValue = PropertyEditorStyle.Normal)]
    public partial PropertyEditorStyle EditorStyle { get; set; }

    [GeneratedStyledProperty]
    public partial object? MenuContent { get; set; }

    [GeneratedStyledProperty]
    public partial IDataTemplate? MenuContentTemplate { get; set; }

    [GeneratedStyledProperty(DefaultValue = 0)]
    public partial float KeyFrameIndex { get; set; }

    public int KeyFrameCount
    {
        get => GetValue(KeyFrameCountProperty);
        set => SetValue(KeyFrameCountProperty, value);
    }

    public Control? ReorderHandle
    {
        get => GetValue(ReorderHandleProperty);
        private set => SetValue(ReorderHandleProperty, value);
    }

    public event EventHandler? DeleteRequested;

    public event EventHandler<PropertyEditorValueChangedEventArgs> ValueChanged
    {
        add => AddHandler(ValueChangedEvent, value);
        remove => RemoveHandler(ValueChangedEvent, value);
    }

    public event EventHandler<PropertyEditorValueChangedEventArgs> ValueConfirmed
    {
        add => AddHandler(ValueConfirmedEvent, value);
        remove => RemoveHandler(ValueConfirmedEvent, value);
    }

    public virtual void Visit(IPropertyEditorContext context)
    {
    }

    // Shows the context's value in valueProperty, confirms the edits this editor raises and shows the context's value
    // again after each, as one it turned down changes nothing; an earlier connection ends.
    private protected CompositeDisposable ConnectConfirmedValue<TEdit>(
        IValueEditorContext<TEdit> context, AvaloniaProperty<TEdit> valueProperty)
    {
        CompositeDisposable connection = StartConnection();
        TEdit shown = default!;
        context.EditValue.Subscribe(value => shown = value).DisposeWith(connection);
        Bind(valueProperty, context.EditValue.ToBinding()).DisposeWith(connection);
        ConfirmEdits(context, connection, () => SetValue(valueProperty, shown));
        return connection;
    }

    // Also previews the value while the user changes it and shows the value the context kept.
    private protected CompositeDisposable ConnectValue<TEdit>(
        IValueEditorContext<TEdit> context, AvaloniaProperty<TEdit> valueProperty, Func<bool>? relative = null)
    {
        CompositeDisposable connection = ConnectConfirmedValue(context, valueProperty);
        this.AddDisposableHandler(
                ValueChangedEvent,
                (_, _) => SetValue(valueProperty, PreviewEdit(context, AvaloniaObjectExtensions.GetValue(this, valueProperty), relative?.Invoke() ?? false)))
            .DisposeWith(connection);
        return connection;
    }

    // Ends the typing in a text box, for a press on a box that starts a drag, so that what was typed is confirmed in an
    // undo step of its own. The focus goes to the nearest parent that takes it, as for a press on anything else; with
    // none, as in a floated property tab, it goes nowhere rather than stay in the text box.
    internal void EndTextEditing()
    {
        foreach (InputElement ancestor in this.GetVisualAncestors().OfType<InputElement>())
        {
            if (ancestor.Focus(NavigationMethod.Pointer))
                return;
        }

        if (TopLevel.GetTopLevel(this)?.FocusManager is { } focusManager && focusManager.GetFocusedElement() is TextBox)
            focusManager.Focus(null);
    }

    // Ends the earlier connection and returns the one that holds the new connection's subscriptions.
    private protected CompositeDisposable StartConnection()
    {
        _dragSession?.Confirm();
        StopValueDrag();
        HasUnconfirmedEdit = false;
        var connection = new CompositeDisposable();
        _connection.Disposable = connection;
        return connection;
    }

    internal bool HasUnconfirmedEdit { get; private set; }

    internal Func<Control, Action, IPropertyEditorDragSession?>? OpenDragSession { get; set; }

    internal Control? ValueDragOwner => _valueDrag?.Owner;

    internal void BeginValueDrag(Control owner, IPointer pointer, Action stop)
    {
        _valueDrag = (owner, pointer, stop);
        _dragSession = OpenDragSession?.Invoke(owner, StopValueDrag);
    }

    internal void CompleteValueDrag()
    {
        _dragSession?.Confirm();
        _valueDrag = null;
    }

    // An interrupted gesture clears its state before releasing capture, which can call the pointer handlers again.
    private void StopValueDrag()
    {
        if (_valueDrag is not { } drag)
            return;

        _valueDrag = null;
        _dragSession = null;
        HasUnconfirmedEdit = false;
        drag.Stop();
        if (drag.Pointer.Captured == drag.Owner)
            drag.Pointer.Capture(null);
    }

    private protected TEdit PreviewEdit<TEdit>(IValueEditorContext<TEdit> context, TEdit value, bool relative)
    {
        using var drag = _dragSession?.Enter();
        HasUnconfirmedEdit = true;
        return context.PreviewEdit(value, relative);
    }

    private protected TEdit PreviewEdit<TEdit>(IValueEditorContext<TEdit> context, TEdit value, bool relative, int editedComponents)
    {
        using var drag = _dragSession?.Enter();
        HasUnconfirmedEdit = true;
        return context.PreviewEdit(value, relative, editedComponents);
    }

    private protected TEdit PreviewFocusedEdit<TEdit>(IValueEditorContext<TEdit> context, TEdit value)
    {
        using var drag = _dragSession?.Enter();
        HasUnconfirmedEdit = true;
        return context.PreviewFocusedEdit(value);
    }

    // Hands the edits this editor confirms to the context, then runs afterConfirm.
    private protected void ConfirmEdits<TEdit>(
        IValueEditorContext<TEdit> context, CompositeDisposable connection, Action? afterConfirm = null)
    {
        this.AddDisposableHandler(
                ValueConfirmedEvent,
                (s, e) =>
                {
                    if (e.Expect<TEdit>(s) is { } args)
                    {
                        HasUnconfirmedEdit = false;
                        if (_dragSession is null)
                            context.ConfirmEdit(args.OldValue, args.NewValue);
                        afterConfirm?.Invoke();
                    }
                })
            .DisposeWith(connection);
    }

    private void UpdateStyle()
    {
        PseudoClasses.Remove(":compact");
        PseudoClasses.Remove(":list-item");
        PseudoClasses.Remove(":settings");
        switch (EditorStyle)
        {
            case PropertyEditorStyle.Compact:
                PseudoClasses.Add(":compact");
                break;

            case PropertyEditorStyle.ListItem:
                PseudoClasses.Add(":list-item");
                break;

            case PropertyEditorStyle.Settings:
                PseudoClasses.Add(":settings");
                break;
        }
    }

    private protected void UpdateAutoCompact(Size availableSize)
    {
        if (!double.IsInfinity(availableSize.Width))
        {
            if (availableSize.Width <= AutoCompactMaxWidth)
            {
                if (!PseudoClasses.Contains(":compact"))
                {
                    PseudoClasses.Add(":compact");
                }
            }
            else
            {
                if (EditorStyle != PropertyEditorStyle.Compact)
                    PseudoClasses.Remove(":compact");
            }
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == EditorStyleProperty)
        {
            UpdateStyle();
        }
        else if (change.Property == MenuContentProperty)
        {
            if (change.OldValue is ILogical oldChild)
            {
                LogicalChildren.Remove(oldChild);
            }

            if (change.NewValue is ILogical newChild)
            {
                LogicalChildren.Add(newChild);
            }
        }
        else if (change.Property == KeyFrameIndexProperty
            || change.Property == KeyFrameCountProperty)
        {
            UpdateKeyFrameProperty();
        }
        else if (change.Property == DescriptionProperty || change.Property == HoverInfoProperty)
        {
            SetAndRaise(HeaderTooltipProperty, ref _headerTooltip, HoverInfo ?? Description);
        }
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        _eventRevokers.Clear();
        base.OnApplyTemplate(e);
        Button? leftButton = e.NameScope.Find<Button>("PART_LeftButton");
        Button? rightButton = e.NameScope.Find<Button>("PART_RightButton");
        if (leftButton == null ^ rightButton == null)
        {
            throw new Exception("Cannot include only one of the buttons");
        }

        if (leftButton != null && rightButton != null)
        {
            leftButton.AddDisposableHandler(Button.ClickEvent, OnLeftButtonClick).DisposeWith(_eventRevokers);
            rightButton.AddDisposableHandler(Button.ClickEvent, OnRightButtonClick).DisposeWith(_eventRevokers);
        }

        ReorderHandle = e.NameScope.Find<Control>("PART_ReorderHandle");
        Button? deleteButton = e.NameScope.Find<Button>("PART_DeleteButton");
        deleteButton?.AddDisposableHandler(Button.ClickEvent, OnDeleteButtonClick).DisposeWith(_eventRevokers);
    }

    private void OnDeleteButtonClick(object? sender, RoutedEventArgs e)
    {
        DeleteRequested?.Invoke(this, e);
    }

    private void OnLeftButtonClick(object? sender, RoutedEventArgs e)
    {
        float value = MathF.Ceiling(KeyFrameIndex) - 1;
        if (0 <= value)
        {
            KeyFrameIndex = value;
        }
    }

    private void OnRightButtonClick(object? sender, RoutedEventArgs e)
    {
        float value = MathF.Floor(KeyFrameIndex) + 1;
        if (value < KeyFrameCount)
        {
            KeyFrameIndex = value;
        }
    }

    private void UpdateKeyFrameProperty()
    {
        PseudoClasses.Remove(":visible-left-button");
        PseudoClasses.Remove(":visible-right-button");

        if (0 < KeyFrameIndex)
        {
            PseudoClasses.Add(":visible-left-button");
        }

        if (KeyFrameIndex < KeyFrameCount - 1)
        {
            PseudoClasses.Add(":visible-right-button");
        }
    }
}
