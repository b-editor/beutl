using Avalonia.Controls;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.PropertyEditors.ViewModels;

namespace Beutl.Editor.Components.PropertyEditors.Views;

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
        if (e.Expect<string?>(sender) is not { } args) return;

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
