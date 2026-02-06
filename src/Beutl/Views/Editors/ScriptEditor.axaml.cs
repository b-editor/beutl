using Avalonia.Controls;

using Beutl.Controls.PropertyEditors;
using Beutl.ViewModels.Editors;

namespace Beutl.Views.Editors;

public partial class ScriptEditor : UserControl
{
    public ScriptEditor()
    {
        InitializeComponent();
        ScriptStringEditor.AddHandler(PropertyEditor.ValueConfirmedEvent, OnValueConfirmed);
        ScriptStringEditor.AddHandler(PropertyEditor.ValueChangedEvent, OnValueChanged);
    }

    private void OnValueConfirmed(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (DataContext is not ScriptEditorViewModel { IsDisposed: false } vm) return;
        if (e is not PropertyEditorValueChangedEventArgs<string?> args) return;

        vm.SetValue(args.OldValue, args.NewValue);
    }

    private void OnValueChanged(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (DataContext is not ScriptEditorViewModel { IsDisposed: false } vm) return;

        if (sender is StringEditor editor)
        {
            editor.Text = vm.SetCurrentValueAndGetCoerced(editor.Text);
        }
    }
}
