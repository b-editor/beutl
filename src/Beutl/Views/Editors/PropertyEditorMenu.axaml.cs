using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;

using Beutl.Animation;
using Beutl.Framework;
using Beutl.Operation;
using Beutl.ProjectSystem;
using Beutl.Styling;
using Beutl.ViewModels;
using Beutl.ViewModels.Editors;
using Beutl.ViewModels.Tools;
using Beutl.Views.Tools;

using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Views.Editors;

public sealed partial class PropertyEditorMenu : UserControl
{
    public PropertyEditorMenu()
    {
        InitializeComponent();
    }

    private void Button_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is BaseEditorViewModel viewModel)
        {
            if (!viewModel.HasAnimation.Value && sender is Button button)
            {
                button.ContextMenu?.Open();
            }
            else if (viewModel.GetService<Scene>() is { } scene)
            {
                var keyTime = scene.CurrentFrame;
                if (symbolIcon.IsFilled)
                {
                    viewModel.RemoveKeyFrame(keyTime);
                }
                else
                {
                    viewModel.InsertKeyFrame(keyTime);
                }
            }
        }
    }

    private void EditAnimation_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is BaseEditorViewModel viewModel
            && viewModel.GetService<EditViewModel>() is { } editViewModel
            && viewModel.GetAnimation() is IKeyFrameAnimation kfAnimation)
        {
            // タイムラインのタブを開く
            var anmTimelineViewModel = new GraphEditorTabViewModel();
            anmTimelineViewModel.SelectedAnimation.Value = new GraphEditorViewModel(editViewModel, kfAnimation, viewModel.GetService<Element>());
            editViewModel.OpenToolTab(anmTimelineViewModel);
        }
    }

    private void EditInlineAnimation_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is BaseEditorViewModel viewModel
            && viewModel.WrappedProperty is IAbstractAnimatableProperty animatableProperty
            && viewModel.GetService<EditViewModel>() is { } editViewModel
            && viewModel.GetService<Element>() is { } layer
            && editViewModel.FindToolTab<TimelineViewModel>() is { } timeline)
        {
            if (animatableProperty.Animation is not IKeyFrameAnimation)
            {
                Type type = typeof(KeyFrameAnimation<>).MakeGenericType(animatableProperty.Property.PropertyType);
                animatableProperty.Animation = Activator.CreateInstance(type, animatableProperty.Property) as IAnimation;
            }

            // タイムラインのタブを開く
            timeline.AttachInline(animatableProperty, layer);
        }
    }

    private void DeleteSetter_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is BaseEditorViewModel viewModel
            && this.FindLogicalAncestorOfType<StyleEditor>()?.DataContext is StyleEditorViewModel parentViewModel
            && viewModel.WrappedProperty is IStylingSetterPropertyImpl wrapper
            && parentViewModel.Style.Value is Style style)
        {
            style.Setters.BeginRecord<ISetter>()
                .Remove(wrapper.Setter)
                .ToCommand()
                .DoAndRecord(CommandRecorder.Default);
        }
    }
}
