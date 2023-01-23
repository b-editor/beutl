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

    private string _text;
    private string _oldValue;
    private TextBlock _headerTextBlock;
    private TextBox _innerTextBox;
    private ContentPresenter _menuPresenter;
    private bool _shouldBeWrapped;

    public string Text
    {
        get => _text;
        set => SetAndRaise(TextProperty, ref _text, value);
    }

    protected TextBox InnerTextBox => _innerTextBox;

    Type IStyleable.StyleKey => typeof(StringEditor);

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _innerTextBox = e.NameScope.Get<TextBox>("PART_InnerTextBox");
        _innerTextBox.GotFocus += (_, e) => OnTextBoxGotFocus(e);
        _innerTextBox.LostFocus += (_, e) => OnTextBoxLostFocus(e);
        _innerTextBox.GetPropertyChangedObservable(TextBox.TextProperty).Subscribe(e =>
        {
            if (e is AvaloniaPropertyChangedEventArgs<string> args)
            {
                OnTextBoxTextChanged(args.NewValue.GetValueOrDefault(), args.OldValue.GetValueOrDefault());
            }
        });

        _headerTextBlock = e.NameScope.Get<TextBlock>("PART_HeaderTextBlock");
        _menuPresenter = e.NameScope.Get<ContentPresenter>("PART_MenuContentPresenter");
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Size arranged = base.ArrangeOverride(finalSize);

        if (_shouldBeWrapped)
        {
            Size headerSize = _headerTextBlock.DesiredSize;
            Size menuSize = _menuPresenter.DesiredSize;
            Size textboxSize = _innerTextBox.DesiredSize;

            // 横に並べたときavailableSizeをはみ出す
            _headerTextBlock.Arrange(new Rect(default, headerSize));

            double menuTop = new Rect(textboxSize).CenterRect(new Rect(menuSize)).Top
                + headerSize.Height;

            _menuPresenter.Arrange(new Rect(new Point(arranged.Width - menuSize.Width, menuTop), menuSize));

            _innerTextBox.Arrange(new Rect(0, headerSize.Height, arranged.Width - menuSize.Width, textboxSize.Height));
            arranged = new Size(
                finalSize.Width,
                headerSize.Height + Math.Max(textboxSize.Height, menuSize.Height));
        }

        return arranged;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (!double.IsInfinity(availableSize.Width))
        {
            Size measured = base.MeasureOverride(availableSize);
            _headerTextBlock.Measure(Size.Infinity);
            _innerTextBox.Measure(Size.Infinity);
            _menuPresenter.Measure(Size.Infinity);

            Size headerSize = _headerTextBlock.DesiredSize;
            Size menuSize = _menuPresenter.DesiredSize;
            Size textboxSize = _innerTextBox.DesiredSize;
            double w = headerSize.Width + textboxSize.Width + menuSize.Width;
            if (w > measured.Width)
            {
                _shouldBeWrapped = true;
                return new Size(
                    measured.Width,
                    headerSize.Height + Math.Max(textboxSize.Height, menuSize.Height));
            }
        }

        _shouldBeWrapped = false;
        return base.MeasureOverride(availableSize);
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
