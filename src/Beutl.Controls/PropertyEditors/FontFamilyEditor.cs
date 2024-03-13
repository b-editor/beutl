using System.ComponentModel;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;

using FluentAvalonia.Core;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Controls.Primitives;

namespace Beutl.Controls.PropertyEditors;

public sealed class FontFamilyPickerFlyout : PickerFlyoutBase
{
    public FontFamilyPicker Picker => _picker ??= new FontFamilyPicker();

    public event TypedEventHandler<FontFamilyPickerFlyout, object> Confirmed;

    public event TypedEventHandler<FontFamilyPickerFlyout, object> Dismissed;

    protected override Control CreatePresenter()
    {
        var pfp = new PickerFlyoutPresenter()
        {
            Content = Picker
        };
        pfp.Confirmed += OnFlyoutConfirmed;
        pfp.Dismissed += OnFlyoutDismissed;

        return pfp;
    }

    protected override void OnConfirmed()
    {
        Confirmed?.Invoke(this, EventArgs.Empty);
        Hide();
    }

    protected override void OnOpening(CancelEventArgs args)
    {
        base.OnOpening(args);
    }

    protected override bool ShouldShowConfirmationButtons() => _showButtons;

    private void OnFlyoutDismissed(PickerFlyoutPresenter sender, object args)
    {
        Dismissed?.Invoke(this, EventArgs.Empty);
        Hide();
    }

    private void OnFlyoutConfirmed(PickerFlyoutPresenter sender, object args)
    {
        OnConfirmed();
    }

    internal void ShowHideButtons(bool show)
    {
        _showButtons = show;
    }

    private bool _showButtons = true;
    private FontFamilyPicker _picker;
}

public class FontFamilyPicker : TemplatedControl
{
    public static readonly StyledProperty<Media.FontFamily> SelectedItemProperty =
        AvaloniaProperty.Register<FontFamilyPicker, Media.FontFamily>(
            nameof(SelectedItem),
            defaultValue: Media.FontFamily.Default,
            defaultBindingMode: BindingMode.TwoWay);
    private TextBox _textBox;
    private ListBox _listBox;
    private IDisposable _disposable;

    public Media.FontFamily SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_textBox != null)
        {
            _textBox.Text = "";
        }

        _listBox?.ScrollIntoView(_listBox.SelectedItem);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        _disposable?.Dispose();
        base.OnApplyTemplate(e);
        _textBox = e.NameScope.Get<TextBox>("PART_SearchTextBox");
        _listBox = e.NameScope.Get<ListBox>("PART_ListBox");
        _listBox.ItemsSource = Media.FontManager.Instance.FontFamilies;
        _disposable = _textBox.GetObservable(TextBox.TextProperty).Subscribe(OnSearchTextBoxTextChanged);
    }

    private void OnSearchTextBoxTextChanged(string obj)
    {
        if (string.IsNullOrWhiteSpace(obj))
        {
            _listBox.ItemsSource = Media.FontManager.Instance.FontFamilies;
        }
        else
        {
            string[] segments = obj.Split(' ')
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToArray();

            _listBox.ItemsSource = Media.FontManager.Instance.FontFamilies.Where(x =>
            {
                foreach (string item in segments)
                {
                    if (x.Name.Contains(item, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                return false;
            });
        }
    }
}

public class BeutlFontFamilyToAvaloniaConverter : FuncValueConverter<Media.FontFamily, Avalonia.Media.FontFamily>
{
    public static readonly BeutlFontFamilyToAvaloniaConverter Instance = new();

    public BeutlFontFamilyToAvaloniaConverter()
        : base(f => f != null ? new Avalonia.Media.FontFamily(f.Name) : Avalonia.Media.FontFamily.Default)
    {
    }
}

public class FontFamilyEditor : PropertyEditor
{
    public static readonly StyledProperty<Media.FontFamily> ValueProperty =
        AvaloniaProperty.Register<FontFamilyEditor, Media.FontFamily>(
            nameof(Value),
            defaultValue: Media.FontFamily.Default,
            defaultBindingMode: BindingMode.TwoWay);

    private static FontFamilyPickerFlyout s_flyout;
    private DropDownButton _button;
    private bool _flyoutActive;

    public Media.FontFamily Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _button = e.NameScope.Get<DropDownButton>("PART_InnerButton");
        _button.AddDisposableHandler(Button.ClickEvent, OnButtonClick);
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

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        s_flyout ??= new FontFamilyPickerFlyout();
        s_flyout.Closed += OnFlyoutClosed;
        s_flyout.Confirmed += OnFlyoutConfirmed;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        s_flyout.Closed -= OnFlyoutClosed;
        s_flyout.Confirmed -= OnFlyoutConfirmed;
    }

    private void OnButtonClick(object sender, RoutedEventArgs e)
    {
        s_flyout.Picker.SelectedItem = Value;
        s_flyout.ShowAt(_button);
        _flyoutActive = true;
    }

    private void OnFlyoutConfirmed(FontFamilyPickerFlyout sender, object args)
    {
        if (_flyoutActive)
        {
            Media.FontFamily oldValue = Value;
            Value = s_flyout.Picker.SelectedItem;
            RaiseEvent(new PropertyEditorValueChangedEventArgs<Media.FontFamily>(Value, oldValue, ValueConfirmedEvent));
        }
    }

    private void OnFlyoutClosed(object sender, EventArgs e)
    {
        _flyoutActive = false;
    }
}
