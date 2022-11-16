using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;

using Beutl.Commands;
using Beutl.Framework;
using Beutl.Services.Editors.Wrappers;
using Beutl.Styling;
using Beutl.ViewModels;
using Beutl.ViewModels.Editors;
using Beutl.ViewModels.Tools;
using Beutl.Views.Tools;

namespace Beutl.Views.Editors;

public sealed partial class EditorBadge : UserControl
{
    public EditorBadge()
    {
        InitializeComponent();
    }

    private void Button_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            button.ContextMenu?.Open();
        }
    }

    private void EditAnimation_Click(object? sender, RoutedEventArgs e)
    {
        if (this.FindLogicalAncestorOfType<EditView>()?.DataContext is EditViewModel editViewModel
            && DataContext is BaseEditorViewModel viewModel
            && viewModel.WrappedProperty is IAbstractAnimatableProperty animatableProperty)
        {
            // 右側のタブを開く
            AnimationTabViewModel anmViewModel
                = editViewModel.FindToolTab<AnimationTabViewModel>()
                    ?? new AnimationTabViewModel();

            anmViewModel.Animation.Value = animatableProperty;

            editViewModel.OpenToolTab(anmViewModel);

            // タイムラインのタブを開く
            AnimationTimelineViewModel? anmTimelineViewModel =
                editViewModel.FindToolTab<AnimationTimelineViewModel>(x => ReferenceEquals(x.WrappedProperty, animatableProperty));

            anmTimelineViewModel ??= new AnimationTimelineViewModel(animatableProperty, editViewModel)
            {
                IsSelected =
                {
                    Value = true
                }
            };

            editViewModel.OpenToolTab(anmTimelineViewModel);
        }
    }

    private void EditInlineAnimation_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is BaseEditorViewModel viewModel
            && viewModel.WrappedProperty is IAbstractAnimatableProperty animatableProperty
            && this.FindLogicalAncestorOfType<EditView>()?.DataContext is EditViewModel editViewModel
            && this.FindLogicalAncestorOfType<StreamOperatorsTab>()?.DataContext is StreamOperatorsTabViewModel { Layer.Value: { } layer }
            && editViewModel.FindToolTab<TimelineViewModel>() is { } timeline)
        {
            // 右側のタブを開く
            AnimationTabViewModel anmViewModel
                = editViewModel.FindToolTab<AnimationTabViewModel>()
                    ?? new AnimationTabViewModel();

            anmViewModel.Animation.Value = animatableProperty;

            editViewModel.OpenToolTab(anmViewModel);

            // タイムラインのタブを開く
            timeline.AttachInline(animatableProperty, layer);
        }
    }

    private void DeleteSetter_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is BaseEditorViewModel viewModel
            && this.FindLogicalAncestorOfType<StyleEditor>()?.DataContext is StyleEditorViewModel parentViewModel
            && viewModel.WrappedProperty is IStylingSetterWrapper wrapper
            && parentViewModel.Style.Value is Style style)
        {
            style.Setters.BeginRecord<ISetter>()
                .Remove(wrapper.Setter)
                .ToCommand()
                .DoAndRecord(CommandRecorder.Default);
        }
    }
}
