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
            .Select(v => (v as BaseEditorViewModel)?.HasAnimation ?? Observable.ReturnThenNever(false))
            .Switch()
            .Select(v => v ? $"- {Message.RightClickToShowMenu}\n- {Message.AnimationIsEnabled}" : null));
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
            if (!viewModel.HasAnimation.Value && sender is Button button)
            {
                button.ContextFlyout?.ShowAt(button);
            }
            else if (viewModel.GetService<EditViewModel>() is { } editViewModel)
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

    private async void EditExpression_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is BaseEditorViewModel { IsDisposed: false } viewModel)
        {
            string? currentExpression = viewModel.GetExpressionString();

            var dialog = new ContentDialog
            {
                Title = Strings.EditExpression,
                PrimaryButtonText = Strings.OK,
                CloseButtonText = Strings.Cancel,
                DefaultButton = ContentDialogButton.Primary
            };

            var textBox = new TextBox
            {
                Text = currentExpression ?? "",
                Watermark = "Sin(Time * 2 * PI) * 100",
                AcceptsReturn = true,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                MinHeight = 100,
                MaxHeight = 200
            };

            dialog.Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = Strings.ExpressionHelp,
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    },
                    textBox
                }
            };

            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(textBox.Text))
            {
                viewModel.SetExpression(textBox.Text);
            }
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
