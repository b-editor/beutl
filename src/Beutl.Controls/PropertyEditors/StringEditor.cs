using System.Reactive.Disposables;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Presenters;
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

    private readonly CompositeDisposable _disposables = new();
    private string _text;
    private string _oldValue;
    private TextBlock _headerTextBlock;
    private TextBox _innerTextBox;
    private ContentPresenter _menuPresenter;

    public string Text
    {
        get => _text;
        set => SetAndRaise(TextProperty, ref _text, value);
    }

    protected TextBox InnerTextBox => _innerTextBox;

    Type IStyleable.StyleKey => typeof(StringEditor);

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        _disposables.Clear();
        base.OnApplyTemplate(e);
        _innerTextBox = e.NameScope.Get<TextBox>("PART_InnerTextBox");
        _innerTextBox.AddDisposableHandler(GotFocusEvent, (_, e) => OnTextBoxGotFocus(e))
            .DisposeWith(_disposables);
        _innerTextBox.AddDisposableHandler(LostFocusEvent, (_, e) => OnTextBoxLostFocus(e))
            .DisposeWith(_disposables);
        _innerTextBox.GetPropertyChangedObservable(TextBox.TextProperty)
            .Subscribe(e =>
            {
                if (e is AvaloniaPropertyChangedEventArgs<string> args)
                {
                    OnTextBoxTextChanged(args.NewValue.GetValueOrDefault(), args.OldValue.GetValueOrDefault());
                }
            })
            .DisposeWith(_disposables);

        _headerTextBlock = e.NameScope.Get<TextBlock>("PART_HeaderTextBlock");
        _menuPresenter = e.NameScope.Get<ContentPresenter>("PART_MenuContentPresenter");
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        Size measured = base.MeasureOverride(availableSize);
        if (!double.IsInfinity(availableSize.Width))
        {
            _headerTextBlock.Measure(Size.Infinity);
            _innerTextBox.Measure(Size.Infinity);
            _menuPresenter.Measure(Size.Infinity);

            Size headerSize = _headerTextBlock.DesiredSize;
            Size menuSize = _menuPresenter.DesiredSize;
            Size textboxSize = _innerTextBox.DesiredSize;

            double w = headerSize.Width + textboxSize.Width + menuSize.Width;
            if (PseudoClasses.Contains(":compact"))
            {
                if (w < availableSize.Width)
                {
                    PseudoClasses.Remove(":compact");
                }
            }
            else if (w > availableSize.Width)
            {
                PseudoClasses.Add(":compact");
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
            RaiseEvent(new PropertyEditorValueChangedEventArgs<string>(Text, _oldValue, ValueChangedEvent));
        }
    }

    protected virtual void OnTextBoxTextChanged(string newValue, string oldValue)
    {
        RaiseEvent(new PropertyEditorValueChangedEventArgs<string>(newValue, oldValue, ValueChangingEvent));
    }
}
