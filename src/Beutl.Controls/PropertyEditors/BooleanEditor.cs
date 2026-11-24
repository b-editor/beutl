using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Interactivity;
using Beutl.Extensibility;

namespace Beutl.Controls.PropertyEditors;

public partial class BooleanEditor : PropertyEditor, IValueEditorControl<bool>
{
    [GeneratedDirectProperty(DefaultBindingMode = BindingMode.TwoWay)]
    public partial bool Value { get; set; }

    public void Connect(IValueEditorContext<bool> context) => ConnectConfirmedValue(context, ValueProperty);

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        ToggleButton toggleButton = e.NameScope.Get<ToggleButton>("PART_CheckBox");
        toggleButton.Click += OnCheckBoxClick;
    }

    private void OnCheckBoxClick(object? sender, RoutedEventArgs e)
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
