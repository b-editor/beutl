using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;

using Beutl.Controls;
using Beutl.ViewModels.Editors;

namespace Beutl.Views.Editors;

public partial class GraphModelNodeMemberView : UserControl
{
    public GraphModelNodeMemberView()
    {
        Resources["ViewModelToViewConverter"] = ViewModelToViewConverter.Instance;
        InitializeComponent();
        ExpandTransitionHelper.Attach(expandToggle, content, ExpandTransitionHelper.ListItemDuration);
    }

    public void Remove_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is GraphModelNodeMemberViewModel viewModel)
        {
            viewModel.Remove();
        }
    }

    private void RenameClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is GraphModelNodeMemberViewModel viewModel)
        {
            var flyout = new RenameFlyout()
            {
                Text = viewModel.GraphNode.Name
            };

            flyout.Confirmed += OnNameConfirmed;

            flyout.ShowAt(this);
        }
    }

    private void OnNameConfirmed(object? sender, string? e)
    {
        if (sender is RenameFlyout flyout
            && DataContext is GraphModelNodeMemberViewModel viewModel)
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
