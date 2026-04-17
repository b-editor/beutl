using Avalonia.Controls;
using Avalonia.Interactivity;

using Beutl.Controls;
using Beutl.Controls.Converters;
using Beutl.ViewModels.Editors;

namespace Beutl.Views.Editors;

public partial class GraphModelNodeMemberView : UserControl
{
    public GraphModelNodeMemberView()
    {
        Resources["ViewModelToViewConverter"] = PropertyEditorContextToViewConverter.Instance;
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

}
