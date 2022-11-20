using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Xaml.Interactivity;

using Beutl.Controls.Behaviors;
using Beutl.ViewModels;
using Beutl.ViewModels.Tools;

namespace Beutl.Views;

public partial class InlineAnimationLayerHeader : UserControl
{
    private static readonly ITransform s_rotate_180deg_transform;

    static InlineAnimationLayerHeader()
    {
        TransformOperations.Builder builder = TransformOperations.CreateBuilder(1);
        builder.AppendRotate(Math.PI);

        s_rotate_180deg_transform = builder.Build();
    }

    public InlineAnimationLayerHeader()
    {
        InitializeComponent();
        Interaction.GetBehaviors(this).Add(new _DragBehavior()
        {
            DragControl = border,
            Orientation = Orientation.Vertical
        });

        ExpandToggle.GetObservable(ToggleButton.IsCheckedProperty)
            .Subscribe(v => ExpandCollapseChevron.RenderTransform = v == true
                                ? s_rotate_180deg_transform
                                : TransformOperations.Identity);
    }

    private void OpenTab_Click(object? sender, RoutedEventArgs e)
    {
        if (this.FindLogicalAncestorOfType<EditView>()?.DataContext is EditViewModel editViewModel
            && DataContext is InlineAnimationLayerViewModel viewModel)
        {
            // 右側のタブを開く
            AnimationTabViewModel anmViewModel
                = editViewModel.FindToolTab<AnimationTabViewModel>()
                    ?? new AnimationTabViewModel();

            anmViewModel.Animation.Value = viewModel.Property;

            editViewModel.OpenToolTab(anmViewModel);
        }
    }

    private void OpenAnimationTimelineClick(object? sender, RoutedEventArgs e)
    {
        if (this.FindLogicalAncestorOfType<EditView>()?.DataContext is EditViewModel editViewModel
            && DataContext is InlineAnimationLayerViewModel viewModel)
        {
            // タイムラインのタブを開く
            AnimationTimelineViewModel? anmTimelineViewModel =
                editViewModel.FindToolTab<AnimationTimelineViewModel>(x => ReferenceEquals(x.WrappedProperty, viewModel.Property));

            anmTimelineViewModel ??= new AnimationTimelineViewModel(viewModel.Property, editViewModel)
            {
                IsSelected =
                {
                    Value = true
                }
            };

            editViewModel.OpenToolTab(anmTimelineViewModel);
        }
    }

    private sealed class _DragBehavior : GenericDragBehavior
    {
        protected override void OnMoveDraggedItem(ItemsControl? itemsControl, int oldIndex, int newIndex)
        {
            if (AssociatedObject?.DataContext is InlineAnimationLayerViewModel viewModel
                && viewModel.LayerHeader.Value is { } layerHeader)
            {
                layerHeader.Inlines.Move(oldIndex, newIndex);
            }
        }
    }
}
