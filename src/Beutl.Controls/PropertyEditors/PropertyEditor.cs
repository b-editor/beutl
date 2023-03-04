using System.Reactive.Disposables;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;

using Beutl.Framework;

namespace Beutl.Controls.PropertyEditors;

[PseudoClasses(":compact", ":visible-left-button", ":visible-right-button")]
[TemplatePart("PART_LeftButton", typeof(Button))]
[TemplatePart("PART_RightButton", typeof(Button))]
public class PropertyEditor : TemplatedControl, IPropertyEditorContextVisitor
{
    public static readonly StyledProperty<string> HeaderProperty =
        AvaloniaProperty.Register<PropertyEditor, string>(nameof(Header));

    public static readonly StyledProperty<bool> IsReadOnlyProperty =
        TextBox.IsReadOnlyProperty.AddOwner<PropertyEditor>();

    public static readonly StyledProperty<bool> UseCompactProperty =
        AvaloniaProperty.Register<PropertyEditor, bool>(nameof(UseCompact), false);

    public static readonly StyledProperty<object> MenuContentProperty =
        AvaloniaProperty.Register<PropertyEditor, object>(nameof(MenuContent));

    public static readonly StyledProperty<IDataTemplate> MenuContentTemplateProperty =
        AvaloniaProperty.Register<PropertyEditor, IDataTemplate>(nameof(MenuContentTemplate));

    public static readonly StyledProperty<int> KeyFrameIndexProperty =
        AvaloniaProperty.Register<PropertyEditor, int>(nameof(KeyFrameIndex), 0);

    public static readonly StyledProperty<int> KeyFrameCountProperty =
        AvaloniaProperty.Register<PropertyEditor, int>(nameof(KeyFrameCount), 0, coerce: (_, v) => Math.Max(v, 0));

    public static readonly RoutedEvent<PropertyEditorValueChangedEventArgs> ValueChangingEvent =
        RoutedEvent.Register<PropertyEditor, PropertyEditorValueChangedEventArgs>(nameof(ValueChanging), RoutingStrategies.Bubble);

    public static readonly RoutedEvent<PropertyEditorValueChangedEventArgs> ValueChangedEvent =
        RoutedEvent.Register<PropertyEditor, PropertyEditorValueChangedEventArgs>(nameof(ValueChanged), RoutingStrategies.Bubble);

    private readonly CompositeDisposable _eventRevokers = new(2);

    public string Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public bool IsReadOnly
    {
        get => GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    public bool UseCompact
    {
        get => GetValue(UseCompactProperty);
        set => SetValue(UseCompactProperty, value);
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

    public int KeyFrameIndex
    {
        get => GetValue(KeyFrameIndexProperty);
        set => SetValue(KeyFrameIndexProperty, value);
    }

    public int KeyFrameCount
    {
        get => GetValue(KeyFrameCountProperty);
        set => SetValue(KeyFrameCountProperty, value);
    }

    public event EventHandler<PropertyEditorValueChangedEventArgs> ValueChanging
    {
        add => AddHandler(ValueChangingEvent, value);
        remove => RemoveHandler(ValueChangingEvent, value);
    }

    public event EventHandler<PropertyEditorValueChangedEventArgs> ValueChanged
    {
        add => AddHandler(ValueChangedEvent, value);
        remove => RemoveHandler(ValueChangedEvent, value);
    }

    public virtual void Visit(IPropertyEditorContext context)
    {
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == UseCompactProperty)
        {
            PseudoClasses.Remove(":compact");
            if (UseCompact)
            {
                PseudoClasses.Add(":compact");
            }
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
    }

    private void OnLeftButtonClick(object sender, RoutedEventArgs e)
    {
        int value = KeyFrameIndex - 1;
        if (0 <= value)
        {
            KeyFrameIndex = value;
        }
    }

    private void OnRightButtonClick(object sender, RoutedEventArgs e)
    {
        int value = KeyFrameIndex + 1;
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
