using System.Reactive.Disposables;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;

using Beutl.Reactive;

namespace Beutl.Controls.PropertyEditors;

[TemplatePart("PART_InnerTextBox", typeof(TextBox))]
public class StringEditor : PropertyEditor
{
    public static readonly DirectProperty<StringEditor, string> TextProperty =
        AvaloniaProperty.RegisterDirect<StringEditor, string>(
            nameof(Text),
            o => o.Text,
            (o, v) => o.Text = v,
            defaultBindingMode: BindingMode.TwoWay);

    private readonly CompositeDisposable _disposables = [];
    private string _text;
    private string _oldValue;

    public string Text
    {
        get => _text;
        set => SetAndRaise(TextProperty, ref _text, value);
    }

    protected TextBox InnerTextBox { get; private set; }

    protected override Type StyleKeyOverride => typeof(StringEditor);

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        _disposables.Clear();
        base.OnApplyTemplate(e);
        InnerTextBox = e.NameScope.Get<TextBox>("PART_InnerTextBox");
        InnerTextBox.AddDisposableHandler(GotFocusEvent, (_, e) => OnTextBoxGotFocus(e))
            .DisposeWith(_disposables);
        InnerTextBox.AddDisposableHandler(LostFocusEvent, (_, e) => OnTextBoxLostFocus(e))
            .DisposeWith(_disposables);
        InnerTextBox.GetPropertyChangedObservable(TextBox.TextProperty)
            .Subscribe(e =>
            {
                if (e is AvaloniaPropertyChangedEventArgs<string> args)
                {
                    OnTextBoxTextChanged(args.NewValue.GetValueOrDefault(), args.OldValue.GetValueOrDefault());
                }
            })
            .DisposeWith(_disposables);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        Size measured = base.MeasureOverride(availableSize);
        if (!double.IsInfinity(availableSize.Width))
        {
            if (availableSize.Width <= 224)
            {
                if (!PseudoClasses.Contains(":compact"))
                {
                    PseudoClasses.Add(":compact");
                }
            }
            else
            {
                if (EditorStyle != PropertyEditorStyle.Compact)
                    PseudoClasses.Remove(":compact");
            }
        }

        return measured;
    }

    protected virtual void OnTextBoxGotFocus(GotFocusEventArgs e)
    {
        _oldValue = Text;
    }

    protected virtual void OnTextBoxLostFocus(RoutedEventArgs e)
    {
        if (Text != _oldValue)
        {
            RaiseEvent(new PropertyEditorValueChangedEventArgs<string>(Text, _oldValue, ValueConfirmedEvent));
        }
    }

    protected virtual void OnTextBoxTextChanged(string newValue, string oldValue)
    {
        if (InnerTextBox?.IsKeyboardFocusWithin == true)
        {
            RaiseEvent(new PropertyEditorValueChangedEventArgs<string>(newValue, oldValue, ValueChangedEvent));
        }
    }
}
