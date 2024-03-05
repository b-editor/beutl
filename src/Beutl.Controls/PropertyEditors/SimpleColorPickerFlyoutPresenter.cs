#nullable enable

using System.Reactive.Disposables;

using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;

using Beutl.Reactive;


namespace Beutl.Controls.PropertyEditors;

public class SimpleColorPickerFlyoutPresenter : DraggablePickerFlyoutPresenter
{
    private readonly CompositeDisposable _disposables = [];
    private const string Palette = ":palette";
    private RadioButton? _spectrumTabButton;
    private RadioButton? _paletteTabButton;
    private Control? _paletteContent;
    private ContentPresenter? _contentPresenter;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        _disposables.Clear();
        base.OnApplyTemplate(e);
        _spectrumTabButton = e.NameScope.Find<RadioButton>("SpectrumTabButton");
        _paletteTabButton = e.NameScope.Find<RadioButton>("PaletteTabButton");
        _paletteContent = e.NameScope.Find<Control>("PaletteContent");
        _contentPresenter = e.NameScope.Find<ContentPresenter>("ContentPresenter");

        _spectrumTabButton?.AddDisposableHandler(ToggleButton.IsCheckedChangedEvent, OnTabButtonIsCheckedChanged)
            .DisposeWith(_disposables);

        _paletteTabButton?.AddDisposableHandler(ToggleButton.IsCheckedChangedEvent, OnTabButtonIsCheckedChanged)
            .DisposeWith(_disposables);

        if (_spectrumTabButton != null)
        {
            _spectrumTabButton.IsChecked = true;
        }
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
