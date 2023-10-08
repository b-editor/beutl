using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Interactivity;

namespace Beutl.Controls.PropertyEditors;

public class BooleanEditor : PropertyEditor
{
    public static readonly DirectProperty<BooleanEditor, bool> ValueProperty =
        AvaloniaProperty.RegisterDirect<BooleanEditor, bool>(
            nameof(Value),
            o => o.Value,
            (o, v) => o.Value = v,
            defaultBindingMode: BindingMode.TwoWay);

    private bool _value;

    public bool Value
    {
        get => _value;
        set => SetAndRaise(ValueProperty, ref _value, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        ToggleButton toggleButton = e.NameScope.Get<ToggleButton>("PART_CheckBox");
        toggleButton.Click += OnCheckBoxClick;
    }

    private void OnCheckBoxClick(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton toggleButton
            && toggleButton.IsChecked.HasValue)
        {
            bool value = toggleButton.IsChecked.Value;
            RaiseEvent(new PropertyEditorValueChangedEventArgs<bool>(value, !value, ValueChangedEvent));
            Value = value;
            RaiseEvent(new PropertyEditorValueChangedEventArgs<bool>(value, !value, ValueConfirmedEvent));

        }
    }
}
