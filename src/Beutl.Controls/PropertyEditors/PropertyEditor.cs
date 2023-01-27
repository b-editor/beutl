using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;

using Beutl.Framework;

namespace Beutl.Controls.PropertyEditors;

[PseudoClasses(":compact")]
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

    public static readonly RoutedEvent<PropertyEditorValueChangedEventArgs> ValueChangingEvent =
        RoutedEvent.Register<PropertyEditor, PropertyEditorValueChangedEventArgs>(nameof(ValueChanging), RoutingStrategies.Bubble);

    public static readonly RoutedEvent<PropertyEditorValueChangedEventArgs> ValueChangedEvent =
        RoutedEvent.Register<PropertyEditor, PropertyEditorValueChangedEventArgs>(nameof(ValueChanged), RoutingStrategies.Bubble);

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
    }
}
