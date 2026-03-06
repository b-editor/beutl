using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;

using Beutl.Controls;
using Beutl.ViewModels.Editors;

namespace Beutl.Views.Editors;

public partial class NodeTreeModelNodeItemView : UserControl
{
    private static readonly CrossFade s_transition = new(TimeSpan.FromMilliseconds(167));
    private CancellationTokenSource? _lastTransitionCts;

    public NodeTreeModelNodeItemView()
    {
        Resources["ViewModelToViewConverter"] = ViewModelToViewConverter.Instance;
        InitializeComponent();
        expandToggle.GetObservable(ToggleButton.IsCheckedProperty)
            .Subscribe(async v =>
            {
                _lastTransitionCts?.Cancel();
                _lastTransitionCts = new CancellationTokenSource();
                CancellationToken localToken = _lastTransitionCts.Token;

                if (v == true)
                {
                    await s_transition.Start(null, content, localToken);
                }
                else
                {
                    await s_transition.Start(content, null, localToken);
                }
            });
    }

    public void Remove_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is NodeTreeModelNodeItemViewModel viewModel)
        {
            viewModel.Remove();
        }
    }

    private void RenameClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is NodeTreeModelNodeItemViewModel viewModel)
        {
            var flyout = new RenameFlyout()
            {
                Text = viewModel.Node.Name
            };

            flyout.Confirmed += OnNameConfirmed;

            flyout.ShowAt(this);
        }
    }

    private void OnNameConfirmed(object? sender, string? e)
    {
        if (sender is RenameFlyout flyout
            && DataContext is NodeTreeModelNodeItemViewModel viewModel)
        {
            flyout.Confirmed -= OnNameConfirmed;
            viewModel.UpdateName(e);
        }
    }

    private sealed class ViewModelToViewConverter : IValueConverter
    {
        public static readonly ViewModelToViewConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is IPropertyEditorContext viewModel)
            {
                if (viewModel.Extension.TryCreateControl(viewModel, out var control))
                {
                    return control;
                }
                else
                {
                    return new Label
                    {
                        Height = 24,
                        Margin = new Thickness(0, 4),
                        Content = viewModel.Extension.DisplayName
                    };
                }
            }
            else
            {
                return BindingNotification.Null;
            }
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return BindingNotification.Null;
        }
    }
}
