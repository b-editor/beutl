using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Xaml.Interactivity;

using Beutl.Controls;
using Beutl.Controls.Behaviors;
using Beutl.Editor.Components.NodeTreeInputTab.ViewModels;

using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Editor.Components.NodeTreeInputTab.Views;

public partial class NodeInputView : UserControl
{
    public NodeInputView()
    {
        Resources["ViewModelToViewConverter"] = ViewModelToViewConverter.Instance;
        InitializeComponent();
        BehaviorCollection collection = Interaction.GetBehaviors(this);
        collection.Add(new _DragBehavior()
        {
            Orientation = Orientation.Vertical,
            DragControl = dragBorder
        });
    }

    public void Remove_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is NodeInputViewModel viewModel2)
        {
            viewModel2.Remove();
        }
    }

    private void RenameClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is NodeInputViewModel viewModel)
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
            && DataContext is NodeInputViewModel viewModel)
        {
            flyout.Confirmed -= OnNameConfirmed;
            viewModel.UpdateName(e);
        }
    }

    private sealed class _DragBehavior : GenericDragBehavior
    {
        protected override void OnMoveDraggedItem(ItemsControl? itemsControl, int oldIndex, int newIndex)
        {
            if (itemsControl?.DataContext is NodeTreeInputTabViewModel { InnerViewModel.Value: { } viewModel })
            {
                HistoryManager history = viewModel.GetRequiredService<HistoryManager>();
                oldIndex = viewModel.ConvertToOriginalIndex(oldIndex);
                newIndex = viewModel.ConvertToOriginalIndex(newIndex);
                viewModel.Model.NodeTree.Nodes.Move(oldIndex, newIndex);
                history.Commit(CommandNames.MoveNode);
            }
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
