using System.Reactive.Disposables;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;

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

[PseudoClasses(":compact", ":list-item", ":settings", ":visible-left-button", ":visible-right-button")]
[TemplatePart("PART_LeftButton", typeof(Button))]
[TemplatePart("PART_RightButton", typeof(Button))]
[TemplatePart("PART_DeleteButton", typeof(Button))]
[TemplatePart("PART_ReorderHandle", typeof(Control))]
public class PropertyEditor : TemplatedControl, IPropertyEditorContextVisitor, IListItemEditor
{
    public static readonly StyledProperty<string> HeaderProperty =
        AvaloniaProperty.Register<PropertyEditor, string>(nameof(Header));
    
    public static readonly StyledProperty<string> DescriptionProperty =
        AvaloniaProperty.Register<PropertyEditor, string>(nameof(Description));

    public static readonly StyledProperty<bool> IsReadOnlyProperty =
        TextBox.IsReadOnlyProperty.AddOwner<PropertyEditor>();

    public static readonly StyledProperty<PropertyEditorStyle> EditorStyleProperty =
        AvaloniaProperty.Register<PropertyEditor, PropertyEditorStyle>(nameof(EditorStyle), PropertyEditorStyle.Normal);

    public static readonly StyledProperty<object> MenuContentProperty =
        AvaloniaProperty.Register<PropertyEditor, object>(nameof(MenuContent));

    public static readonly StyledProperty<IDataTemplate> MenuContentTemplateProperty =
        AvaloniaProperty.Register<PropertyEditor, IDataTemplate>(nameof(MenuContentTemplate));

    public static readonly StyledProperty<float> KeyFrameIndexProperty =
        AvaloniaProperty.Register<PropertyEditor, float>(nameof(KeyFrameIndex), 0);

    public static readonly StyledProperty<int> KeyFrameCountProperty =
        AvaloniaProperty.Register<PropertyEditor, int>(nameof(KeyFrameCount), 0, coerce: (_, v) => Math.Max(v, 0));

    public static readonly StyledProperty<Control> ReorderHandleProperty =
        AvaloniaProperty.Register<PropertyEditor, Control>(nameof(ReorderHandle), null);

    public static readonly RoutedEvent<PropertyEditorValueChangedEventArgs> ValueChangedEvent =
        RoutedEvent.Register<PropertyEditor, PropertyEditorValueChangedEventArgs>(nameof(ValueChanged), RoutingStrategies.Bubble);

    public static readonly RoutedEvent<PropertyEditorValueChangedEventArgs> ValueConfirmedEvent =
        RoutedEvent.Register<PropertyEditor, PropertyEditorValueChangedEventArgs>(nameof(ValueConfirmed), RoutingStrategies.Bubble);

    private readonly CompositeDisposable _eventRevokers = new(3);

    static PropertyEditor()
    {
        MarginProperty.OverrideDefaultValue<PropertyEditor>(new(4, 0));
    }

    public string Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }
    
    public string Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public bool IsReadOnly
    {
        get => GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    public PropertyEditorStyle EditorStyle
    {
        get => GetValue(EditorStyleProperty);
        set => SetValue(EditorStyleProperty, value);
    }

    public object MenuContent
    {
        get => GetValue(MenuContentProperty);
        set => SetValue(MenuContentProperty, value);
    }

    public IDataTemplate MenuContentTemplate
    {
        get => GetValue(MenuContentTemplateProperty);
        set => SetValue(MenuContentTemplateProperty, value);
    }

    public float KeyFrameIndex
    {
        get => GetValue(KeyFrameIndexProperty);
        set => SetValue(KeyFrameIndexProperty, value);
    }

    public int KeyFrameCount
    {
        get => GetValue(KeyFrameCountProperty);
        set => SetValue(KeyFrameCountProperty, value);
    }

    public Control ReorderHandle
    {
        get => GetValue(ReorderHandleProperty);
        private set => SetValue(ReorderHandleProperty, value);
    }

    public event EventHandler DeleteRequested;

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
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        _eventRevokers.Clear();
        base.OnApplyTemplate(e);
        Button leftButton = e.NameScope.Find<Button>("PART_LeftButton");
        Button rightButton = e.NameScope.Find<Button>("PART_RightButton");
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
        Button deleteButton = e.NameScope.Find<Button>("PART_DeleteButton");
        deleteButton?.AddDisposableHandler(Button.ClickEvent, OnDeleteButtonClick).DisposeWith(_eventRevokers);
    }

    private void OnDeleteButtonClick(object sender, RoutedEventArgs e)
    {
        DeleteRequested?.Invoke(this, e);
    }

    private void OnLeftButtonClick(object sender, RoutedEventArgs e)
    {
        float value = MathF.Ceiling(KeyFrameIndex) - 1;
        if (0 <= value)
        {
            KeyFrameIndex = value;
        }
    }

    private void OnRightButtonClick(object sender, RoutedEventArgs e)
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
