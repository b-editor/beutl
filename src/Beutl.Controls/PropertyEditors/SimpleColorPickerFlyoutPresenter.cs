using System.Reactive.Disposables;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;

using Beutl.Reactive;


namespace Beutl.Controls.PropertyEditors;

public class SimpleColorPickerFlyoutPresenter : DraggablePickerFlyoutPresenter
{
    private readonly CompositeDisposable _disposables = [];
    private readonly SerialDisposable _pickerColor = new();
    private const string Palette = ":palette";
    private RadioButton? _spectrumTabButton;
    private RadioButton? _paletteTabButton;
    private Control? _paletteContent;
    private ColorPaletteView? _paletteView;
    private ContentPresenter? _contentPresenter;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        _disposables.Clear();
        if (_paletteView != null)
        {
            _paletteView.ColorSelected -= OnPaletteColorSelected;
        }

        base.OnApplyTemplate(e);
        _spectrumTabButton = e.NameScope.Find<RadioButton>("SpectrumTabButton");
        _paletteTabButton = e.NameScope.Find<RadioButton>("PaletteTabButton");
        _paletteContent = e.NameScope.Find<Control>("PaletteContent");
        _paletteView = e.NameScope.Find<ColorPaletteView>("PaletteView");
        _contentPresenter = e.NameScope.Find<ContentPresenter>("ContentPresenter");

        _spectrumTabButton?.AddDisposableHandler(ToggleButton.IsCheckedChangedEvent, OnTabButtonIsCheckedChanged)
            .DisposeWith(_disposables);

        _paletteTabButton?.AddDisposableHandler(ToggleButton.IsCheckedChangedEvent, OnTabButtonIsCheckedChanged)
            .DisposeWith(_disposables);

        if (_paletteView != null)
        {
            _paletteView.ColorSelected += OnPaletteColorSelected;
        }

        if (_spectrumTabButton != null)
        {
            _spectrumTabButton.IsChecked = true;
        }

        OnContentChanged();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ContentProperty)
        {
            OnContentChanged();
        }
    }

    // The palette picks colors for a SimpleColorPicker only: a grading color is not a color to reuse.
    private void OnContentChanged()
    {
        var picker = Content as SimpleColorPicker;
        _pickerColor.Disposable = picker?.GetObservable(SimpleColorPicker.ColorProperty)
            .Subscribe(color =>
            {
                if (_paletteView != null)
                {
                    _paletteView.CurrentColor = color;
                }
            });

        if (_paletteTabButton != null)
        {
            _paletteTabButton.IsVisible = picker != null;
            if (picker == null && _paletteTabButton.IsChecked == true && _spectrumTabButton != null)
            {
                _spectrumTabButton.IsChecked = true;
            }
        }
    }

    private void OnPaletteColorSelected(ColorPaletteView sender, Color color)
    {
        (Content as SimpleColorPicker)?.SetColor(color);
    }

    private void OnTabButtonIsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (_paletteTabButton != null && _spectrumTabButton != null)
        {
            if (_paletteTabButton.IsChecked == true
                && _paletteContent != null
                && _contentPresenter != null)
            {
                _paletteContent.MaxHeight = _contentPresenter.Bounds.Height;
            }

            PseudoClasses.Set(Palette, _paletteTabButton.IsChecked == true);
        }
    }
}
