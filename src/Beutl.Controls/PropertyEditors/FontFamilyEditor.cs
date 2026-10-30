using System.Reactive.Disposables;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Interactivity;
using Beutl.Extensibility;
using Beutl.Language;
using Beutl.Media;
using Beutl.Reactive;

namespace Beutl.Controls.PropertyEditors;

public class FontFamilyEditor : PropertyEditor, IValueEditorControl<Media.FontFamily?>
{
    public static readonly StyledProperty<Media.FontFamily> ValueProperty =
        AvaloniaProperty.Register<FontFamilyEditor, Media.FontFamily>(
            nameof(Value),
            defaultValue: Media.FontFamily.Default,
            defaultBindingMode: BindingMode.TwoWay);

    private DropDownButton? _button;
    private bool _flyoutActive;

    public Media.FontFamily Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public void Connect(IValueEditorContext<Media.FontFamily?> context)
    {
        CompositeDisposable connection = ConnectConfirmedValue<Media.FontFamily?>(context, ValueProperty!);
        // The picker keeps showing the font it highlights, whichever value the context keeps.
        this.AddDisposableHandler(ValueChangedEvent, (_, e) =>
            {
                if (e is PropertyEditorValueChangedEventArgs<Media.FontFamily?> args)
                    context.PreviewEdit(args.NewValue);
            })
            .DisposeWith(connection);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _button = e.NameScope.Get<DropDownButton>("PART_InnerButton");
        _button.AddDisposableHandler(Button.ClickEvent, OnButtonClick);
        _button.Content = FontFamilyNames.GetDisplayName(Value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        Size measured = base.MeasureOverride(availableSize);
        UpdateAutoCompact(availableSize);
        return measured;
    }

    private Task<Media.FontFamily?> Select()
    {
        var viewModel = new FontFamilyPickerFlyoutViewModel();
        viewModel.SelectedItem.Value = viewModel.Items.FirstOrDefault(f => (Media.FontFamily)f.UserData == Value);
        var prevValue = Value;
        IDisposable preview = viewModel.SelectedItem.Subscribe(item =>
        {
            var value = item?.UserData as Media.FontFamily;
            if (value != prevValue && value != null)
            {
                Value = value;
                RaiseEvent(new PropertyEditorValueChangedEventArgs<Media.FontFamily>(
                    value, prevValue, ValueChangedEvent));
                prevValue = Value;
            }
        });

        SearchablePickerFlyout dialog = CreatePicker(viewModel);
        // A search still waiting for typing to pause can highlight a font after the picker has closed.
        dialog.Closed += (_, _) => preview.Dispose();
        dialog.ShowAt(this);
        var tcs = new TaskCompletionSource<Media.FontFamily?>();
        dialog.Dismissed += (_, _) => tcs.SetResult(null);
        // A search that matches nothing leaves no selection; keep the font being previewed.
        dialog.Confirmed += (_, _) => tcs.SetResult(viewModel.SelectedItem.Value?.UserData as Media.FontFamily ?? prevValue);

        return tcs.Task;
    }

    // The picker with the font theme and the font wording, which the editor and its tests open alike.
    internal static SearchablePickerFlyout CreatePicker(FontFamilyPickerFlyoutViewModel viewModel)
    {
        return new SearchablePickerFlyout(viewModel)
        {
            PresenterThemeKey = "FontFamilyPickerFlyoutPresenter",
            NoResultsText = Strings.FontFamilyPicker_NoResults
        };
    }

    private async void OnButtonClick(object? sender, RoutedEventArgs e)
    {
        if (_flyoutActive) return;

        try
        {
            _flyoutActive = true;
            Media.FontFamily oldValue = Value;
            var newValue = await Select();
            if (newValue == null)
            {
                var value = Value;
                Value = oldValue;
                RaiseEvent(new PropertyEditorValueChangedEventArgs<Media.FontFamily>(
                    oldValue, value, ValueConfirmedEvent));
            }
            else
            {
                Value = newValue;
                RaiseEvent(new PropertyEditorValueChangedEventArgs<Media.FontFamily>(
                    Value, oldValue, ValueConfirmedEvent));
            }
        }
        finally
        {
            _flyoutActive = false;
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValueProperty)
        {
            if (_button != null)
            {
                _button.Content = FontFamilyNames.GetDisplayName(Value);
            }
        }
    }
}
