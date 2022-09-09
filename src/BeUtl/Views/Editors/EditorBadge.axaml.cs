using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;

using BeUtl.Commands;
using BeUtl.Framework;
using BeUtl.Services.Editors.Wrappers;
using BeUtl.Styling;
using BeUtl.ViewModels;
using BeUtl.ViewModels.Editors;
using BeUtl.ViewModels.Tools;
using BeUtl.Views.Tools;

namespace BeUtl.Views.Editors;

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
        if (this.FindLogicalAncestorOfType<EditView>().DataContext is EditViewModel editViewModel
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

            if (anmTimelineViewModel == null)
            {
                anmTimelineViewModel = new AnimationTimelineViewModel(
                    animatableProperty,
                    editViewModel)
                {
                    IsSelected =
                    {
                        Value = true
                    }
                };
            }

            editViewModel.OpenToolTab(anmTimelineViewModel);
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
