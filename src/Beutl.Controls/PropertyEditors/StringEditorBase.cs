using System.Diagnostics.CodeAnalysis;
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

// The text box and theme that StringEditor, the value editors that parse text and StorageFileEditor share.
[TemplatePart("PART_InnerTextBox", typeof(TextBox))]
public abstract class StringEditorBase : PropertyEditor
{
    public static readonly DirectProperty<StringEditorBase, string> TextProperty =
        AvaloniaProperty.RegisterDirect<StringEditorBase, string>(
            nameof(Text),
            o => o.Text,
            (o, v) => o.Text = v,
            defaultBindingMode: BindingMode.TwoWay);

    private readonly CompositeDisposable _disposables = [];
    private string _text = string.Empty;

    [AllowNull]
    public string Text
    {
        get => _text;
        set => SetAndRaise(TextProperty, ref _text, value ?? string.Empty);
    }

    protected TextBox? InnerTextBox { get; set; }

    protected override Type StyleKeyOverride => typeof(StringEditor);

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        _disposables.Clear();
        base.OnApplyTemplate(e);
        InnerTextBox = e.NameScope.Find<TextBox>("PART_InnerTextBox");
        if (InnerTextBox != null)
        {
            InnerTextBox.AddDisposableHandler(GotFocusEvent, (_, e) => OnTextBoxGotFocus(e))
                .DisposeWith(_disposables);
            InnerTextBox.AddDisposableHandler(LostFocusEvent, (_, e) => OnTextBoxLostFocus(e))
                .DisposeWith(_disposables);
            InnerTextBox.GetPropertyChangedObservable(TextBox.TextProperty)
                .Subscribe(e =>
                {
                    if (e is AvaloniaPropertyChangedEventArgs<string> args)
                    {
                        OnTextBoxTextChanged(args.NewValue.GetValueOrDefault() ?? string.Empty,
                            args.OldValue.GetValueOrDefault() ?? string.Empty);
                    }
                })
                .DisposeWith(_disposables);
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        Size measured = base.MeasureOverride(availableSize);
        UpdateAutoCompact(availableSize);
        return measured;
    }

    protected virtual void OnTextBoxGotFocus(FocusChangedEventArgs e)
    {
    }

    protected virtual void OnTextBoxLostFocus(RoutedEventArgs e)
    {
    }

    protected virtual void OnTextBoxTextChanged(string newValue, string oldValue)
    {
    }
}
