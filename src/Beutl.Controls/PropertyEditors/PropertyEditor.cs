using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;

using FluentAvalonia.UI.Controls;

namespace Beutl.Controls.PropertyEditors;

public class PropertyEditor : TemplatedControl
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
}
