using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Beutl.Animation;
using Beutl.ProjectSystem;
using Beutl.ViewModels;
using Beutl.ViewModels.Editors;
using Beutl.ViewModels.Tools;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Views.Editors;

public sealed partial class PropertyEditorMenu : UserControl
{
    public PropertyEditorMenu()
    {
        InitializeComponent();
        Bind(ToolTip.TipProperty, this.GetObservable(DataContextProperty)
            .Select(v => v is BaseEditorViewModel viewModel
                ? viewModel.HasAnimation
                    .CombineLatest(viewModel.HasExpression)
                    .Select(t => t switch
                    {
                        (true, _) =>
                            $"- {Message.RightClickToShowMenu}\n- {Message.AnimationIsEnabled}",
                        (_, true) => $"- {Message.RightClickToShowMenu}\n- {Message.ExpressionIsSet}",
                        _ => null
                    })
                : Observable.ReturnThenNever<string?>(null))
            .Switch());
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        toggleLivePreview.IsVisible = DataContext is IConfigureLivePreview;
        uniformEditorToggle.IsVisible = DataContext is IConfigureUniformEditor;

        // 式の編集メニューはIExpressionPropertyAdapterをサポートするプロパティでのみ表示
        bool supportsExpression = DataContext is BaseEditorViewModel { PropertyAdapter: IExpressionPropertyAdapter };
        expressionSeparator.IsVisible = supportsExpression;
        editExpressionItem.IsVisible = supportsExpression;
        removeExpressionItem.IsVisible = supportsExpression;
    }

    private void Button_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is BaseEditorViewModel { IsDisposed: false } viewModel)
        {
            if (viewModel.HasExpression.Value)
            {
                EditExpression_Click(sender, e);
            }
            else if (viewModel.HasAnimation.Value && viewModel.GetService<EditViewModel>() is { } editViewModel)
            {
                TimeSpan keyTime = editViewModel.CurrentTime.Value;
                if (symbolIcon.IsFilled)
                {
                    viewModel.RemoveKeyFrame(keyTime);
                }
                else
                {
                    viewModel.InsertKeyFrame(keyTime);
                }
            }
            else if (sender is Button button)
            {
                button.ContextFlyout?.ShowAt(button);
            }
        }
    }

    private void EditAnimation_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is BaseEditorViewModel { IsDisposed: false } viewModel
            && viewModel.PropertyAdapter is IAnimatablePropertyAdapter animatableProperty
            && viewModel.GetService<EditViewModel>() is { } editViewModel)
        {
            viewModel.PrepareToEditAnimation();

            // タイムラインのタブを開く
            var anmTimelineViewModel = new GraphEditorTabViewModel(editViewModel);
            anmTimelineViewModel.Element.Value = viewModel.GetService<Element>();
            anmTimelineViewModel.Select(animatableProperty.Animation as KeyFrameAnimation);
            editViewModel.OpenToolTab(anmTimelineViewModel);
        }
    }

    private void RemoveAnimation_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is BaseEditorViewModel { IsDisposed: false } viewModel
            && viewModel.PropertyAdapter is IAnimatablePropertyAdapter { Animation: { } animation }
            && viewModel.GetService<EditViewModel>() is { } editViewModel)
        {
            (editViewModel as ISupportCloseAnimation).Close(animation);
            viewModel.RemoveAnimation();
        }
    }

    private void EditInlineAnimation_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is BaseEditorViewModel { IsDisposed: false } viewModel
            && viewModel.PropertyAdapter is IAnimatablePropertyAdapter animatableProperty
            && viewModel.GetService<EditViewModel>() is { } editViewModel
            && viewModel.GetService<Element>() is { } element
            && editViewModel.FindToolTab<TimelineViewModel>() is { } timeline)
        {
            viewModel.PrepareToEditAnimation();

            if (animatableProperty.Animation is IKeyFrameAnimation)
            {
                // タイムラインのタブを開く
                timeline.AttachInline(animatableProperty, element);
            }
        }
    }

    private void EditExpression_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is BaseEditorViewModel { IsDisposed: false } viewModel)
        {
            string? currentExpression = viewModel.GetExpressionString();

            var flyout = new ExpressionEditorFlyout();
            flyout.Placement = PlacementMode.Bottom;

            flyout.Confirmed += (_, _) =>
            {
                if (!string.IsNullOrWhiteSpace(flyout.ExpressionText))
                {
                    viewModel.SetExpression(flyout.ExpressionText);
                }
            };

            flyout.ShowAt(this);
            flyout.ExpressionText = currentExpression ?? "";
        }
    }

    private void RemoveExpression_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is BaseEditorViewModel { IsDisposed: false } viewModel)
        {
            viewModel.RemoveExpression();
        }
    }
}
