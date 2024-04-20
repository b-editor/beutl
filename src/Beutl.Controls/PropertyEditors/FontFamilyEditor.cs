using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Interactivity;

namespace Beutl.Controls.PropertyEditors;

public class FontFamilyEditor : PropertyEditor
{
    public static readonly StyledProperty<Media.FontFamily> ValueProperty =
        AvaloniaProperty.Register<FontFamilyEditor, Media.FontFamily>(
            nameof(Value),
            defaultValue: Media.FontFamily.Default,
            defaultBindingMode: BindingMode.TwoWay);

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

    private Task<Media.FontFamily> Select()
    {
        var viewModel = new FontFamilyPickerFlyoutViewModel();
        viewModel.SelectedItem.Value = viewModel.Items.FirstOrDefault(f => f.DisplayName == Value.Name);

        var dialog = new FontFamilyPickerFlyout(viewModel);
        dialog.ShowAt(this);
        var tcs = new TaskCompletionSource<Media.FontFamily>();
        dialog.Pinned += (_, item) => viewModel.Pin(item);
        dialog.Unpinned += (_, item) => viewModel.Unpin(item);
        dialog.Dismissed += (_, _) => tcs.SetResult(null);
        dialog.Confirmed += (_, _) => tcs.SetResult(viewModel.SelectedItem.Value?.UserData as Media.FontFamily);

        return tcs.Task;
    }

    private async void OnButtonClick(object sender, RoutedEventArgs e)
    {
        if (_flyoutActive) return;

        try
        {
            _flyoutActive = true;
            var newValue = await Select();
            if (newValue == null) return;
            Media.FontFamily oldValue = Value;
            Value = newValue;
            RaiseEvent(new PropertyEditorValueChangedEventArgs<Media.FontFamily>(Value, oldValue, ValueConfirmedEvent));
        }
        finally
        {
            _flyoutActive = false;
        }
    }
}
