using Avalonia.Input;
using Avalonia.Interactivity;

using Beutl.Extensibility;

namespace Beutl.Controls.PropertyEditors;

public class StringEditor : StringEditorBase, IValueEditorControl<string>
{
    private string _oldValue = string.Empty;

    public void Connect(IValueEditorContext<string> context) => ConnectValue(context, TextProperty);

    protected override void OnTextBoxGotFocus(FocusChangedEventArgs e)
    {
        _oldValue = Text;
    }

    protected override void OnTextBoxLostFocus(RoutedEventArgs e)
    {
        if (Text != _oldValue)
        {
            RaiseEvent(new PropertyEditorValueChangedEventArgs<string>(Text, _oldValue, ValueConfirmedEvent));
        }
    }

    protected override void OnTextBoxTextChanged(string newValue, string oldValue)
    {
        if (InnerTextBox?.IsKeyboardFocusWithin == true)
        {
            RaiseEvent(new PropertyEditorValueChangedEventArgs<string>(newValue, oldValue, ValueChangedEvent));
        }
    }
}
