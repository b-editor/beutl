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
        CheckBox checkBox = e.NameScope.Get<CheckBox>("PART_CheckBox");
        checkBox.Click += OnCheckBoxClick;
    }

    private void OnCheckBoxClick(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkBox
            && checkBox.IsChecked.HasValue)
        {
            bool value = checkBox.IsChecked.Value;
            RaiseEvent(new PropertyEditorValueChangedEventArgs<bool>(value, !value, ValueChangingEvent));
            Value = value;
            RaiseEvent(new PropertyEditorValueChangedEventArgs<bool>(value, !value, ValueChangedEvent));

        }
    }
}
