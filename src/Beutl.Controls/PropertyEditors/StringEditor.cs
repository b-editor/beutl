using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;

namespace Beutl.Controls.PropertyEditors;

[TemplatePart("PART_InnerTextBox", typeof(TextBox))]
public class StringEditor : PropertyEditor, IStyleable
{
    public static readonly DirectProperty<StringEditor, string> TextProperty =
        AvaloniaProperty.RegisterDirect<StringEditor, string>(
            nameof(Text),
            o => o.Text,
            (o, v) => o.Text = v,
            defaultBindingMode: BindingMode.TwoWay);

    private string _text;
    private string _oldValue;

    public string Text
    {
        get => _text;
        set => SetAndRaise(TextProperty, ref _text, value);
    }
    protected TextBox InnerTextBox { get; private set; }

    Type IStyleable.StyleKey => typeof(StringEditor);

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        InnerTextBox = e.NameScope.Get<TextBox>("PART_InnerTextBox");
        InnerTextBox.GotFocus += (_, e) => OnTextBoxGotFocus(e);
        InnerTextBox.LostFocus += (_, e) => OnTextBoxLostFocus(e);
        InnerTextBox.GetPropertyChangedObservable(TextBox.TextProperty).Subscribe(e =>
        {
            if (e is AvaloniaPropertyChangedEventArgs<string> args)
            {
                OnTextBoxTextChanged(args.NewValue.GetValueOrDefault(), args.OldValue.GetValueOrDefault());
            }
        });
    }

    protected virtual void OnTextBoxGotFocus(GotFocusEventArgs e)
    {
        _oldValue = Text;
    }

    protected virtual void OnTextBoxLostFocus(RoutedEventArgs e)
    {
        if (Text != _oldValue)
        {
            RaiseEvent(new PropertyEditorValueChangedEventArgs<string>(Text, _oldValue, ValueChangedEvent));
        }
    }

    protected virtual void OnTextBoxTextChanged(string newValue, string oldValue)
    {
        RaiseEvent(new PropertyEditorValueChangedEventArgs<string>(newValue, oldValue, ValueChangingEvent));
    }
}
