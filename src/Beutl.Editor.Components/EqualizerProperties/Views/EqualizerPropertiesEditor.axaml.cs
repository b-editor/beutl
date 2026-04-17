using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;
using Beutl.Audio.Effects;
using Beutl.Editor.Components.EqualizerProperties.ViewModels;

namespace Beutl.Editor.Components.EqualizerProperties.Views;

public sealed partial class EqualizerPropertiesEditor : UserControl
{
    private EqualizerPropertiesViewModel? _viewModel;
    private EqualizerEffect? _currentEffect;

    public EqualizerPropertiesEditor()
    {
        Resources["ViewModelToViewConverter"] = ViewModelToViewConverterImpl.Instance;
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        _viewModel = DataContext as EqualizerPropertiesViewModel;
        RefreshCurveBands();
        if (_viewModel != null)
        {
            _viewModel.Bands.CollectionChanged += (_, _) => RefreshCurveBands();
        }
    }

    private void RefreshCurveBands()
    {
        var effect = _viewModel?.TryGetEqualizerEffect();
        if (!ReferenceEquals(effect, _currentEffect))
        {
            _currentEffect = effect;
        }
        var curve = this.FindControl<EqualizerCurveEditor>("curveEditor");
        if (curve != null)
        {
            curve.Bands = effect?.Bands;
            curve.NotifyBandsChanged();
        }
    }

    private void OnBandChanged(object? sender, EqualizerBandEventArgs e)
    {
        _viewModel?.SetBandValueLive(e.BandIndex, e.Property, e.NewValue);
    }

    private void OnBandConfirmed(object? sender, EqualizerBandEventArgs e)
    {
        _viewModel?.CommitBandValue(e.BandIndex, e.Property, e.OldValue, e.NewValue);
    }

    private sealed class ViewModelToViewConverterImpl : IValueConverter
    {
        public static readonly ViewModelToViewConverterImpl Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        {
            if (value is IPropertyEditorContext viewModel)
            {
                if (viewModel.Extension.TryCreateControl(viewModel, out var control))
                {
                    return control;
                }
                return new Label
                {
                    Height = 24,
                    Margin = new Thickness(0, 4),
                    Content = viewModel.Extension.DisplayName
                };
            }
            return BindingNotification.Null;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        {
            return BindingNotification.Null;
        }
    }
}
