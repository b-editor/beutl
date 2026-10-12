using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Beutl.Animation;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.GraphEditorTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.ProjectSystem;
using Beutl.ViewModels;
using Beutl.ViewModels.Editors;
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
                            $"- {MessageStrings.RightClickToShowMenu}\n- {MessageStrings.AnimationIsEnabled}",
                        (_, true) => $"- {MessageStrings.RightClickToShowMenu}\n- {MessageStrings.ExpressionIsSet}",
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

        bool supportsAnimation = DataContext is BaseEditorViewModel { PropertyAdapter: IAnimatablePropertyAdapter };
        editAnimationItem.IsVisible = supportsAnimation;
        editInlineAnimationItem.IsVisible = supportsAnimation;
        removeAnimationItem.IsVisible = supportsAnimation;

        bool supportsExpression = DataContext is BaseEditorViewModel { PropertyAdapter: IExpressionPropertyAdapter };
        expressionSeparator.IsVisible = supportsExpression;
        editExpressionItem.IsVisible = supportsExpression;
        removeExpressionItem.IsVisible = supportsExpression;

        bool isEngineProperty = (DataContext as BaseEditorViewModel)?.PropertyAdapter.GetEngineProperty() != null;
        copyPropertyPathSeparator.IsVisible = isEngineProperty;
        copyPropertyPathItem.IsVisible = isEngineProperty;
        copyGetPropertyCodeItem.IsVisible = isEngineProperty;
    }

    private void Button_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is BaseEditorViewModel { IsDisposed: false } viewModel)
        {
            if (!viewModel.CanEditProperty.Value)
            {
                // Keep the copy actions available on a locked clip.
                if (sender is Button readOnlyButton)
                    readOnlyButton.ContextFlyout?.ShowAt(readOnlyButton);
                return;
            }

            if (viewModel.HasExpression.Value)
            {
                EditExpression_Click(sender, e);
            }
            else if (viewModel.HasAnimation.Value && viewModel.GetService<IEditorClock>() is { } editorClock)
            {
                TimeSpan keyTime = editorClock.CurrentTime.Value;
                if (symbolIcon.IconVariant == FluentIcons.Common.IconVariant.Filled)
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
        if (DataContext is BaseEditorViewModel { IsDisposed: false, CanEditProperty.Value: true } viewModel
            && viewModel.PropertyAdapter is IAnimatablePropertyAdapter animatableProperty
            && viewModel.GetService<EditViewModel>() is { } editViewModel)
        {
            viewModel.PrepareToEditAnimation();

            // Shows the animation in a graph editor tab.
            Element? element = viewModel.GetService<Element>();
            var animation = animatableProperty.Animation as KeyFrameAnimation;
            GraphEditorTabViewModel graphEditorTab =
                GraphEditorTabViewModel.FindReusable(editViewModel, element, animation)
                ?? new GraphEditorTabViewModel(editViewModel);
            graphEditorTab.Element.Value = element;
            graphEditorTab.Select(animation);
            editViewModel.OpenToolTab(graphEditorTab);
        }
    }

    private void RemoveAnimation_Click(object? sender, RoutedEventArgs e)
    {
        // Also a repair on a recovery-protected element, so it follows CanRemoveAnimation.
        if (DataContext is BaseEditorViewModel { IsDisposed: false, CanRemoveAnimation.Value: true } viewModel)
        {
            viewModel.RemoveAnimation();
        }
    }

    private void EditInlineAnimation_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is BaseEditorViewModel { IsDisposed: false, CanEditProperty.Value: true } viewModel
            && viewModel.PropertyAdapter is IAnimatablePropertyAdapter animatableProperty
            && viewModel.GetService<EditViewModel>() is { } editViewModel
            && viewModel.GetService<Element>() is { } element)
        {
            viewModel.PrepareToEditAnimation();

            if (animatableProperty.Animation is IKeyFrameAnimation
                && FindOrOpenTimeline(editViewModel) is { } timeline)
            {
                timeline.AttachInline(animatableProperty, element);
            }
        }
    }

    // The inline view lives in the timeline, so a closed timeline is opened first, the way
    // "Edit animation" opens its graph editor tab.
    private static TimelineTabViewModel? FindOrOpenTimeline(EditViewModel editViewModel)
    {
        if (editViewModel.FindToolTab<TimelineTabViewModel>() is { } timeline)
            return timeline;

        var created = new TimelineTabViewModel(editViewModel);
        if (editViewModel.OpenToolTab(created))
            return created;

        created.Dispose();
        return null;
    }

    private void EditExpression_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is BaseEditorViewModel { IsDisposed: false, CanEditProperty.Value: true } viewModel)
        {
            string? currentExpression = viewModel.GetExpressionString();

            var flyout = new ExpressionEditorFlyout();
            flyout.Placement = PlacementMode.BottomEdgeAlignedRight;
            flyout.ExpressionText = currentExpression ?? "";
            flyout.ErrorMessage = viewModel.PropertyAdapter.GetEngineProperty()?.ExpressionError;
            flyout.Confirmed += (_, args) =>
            {
                bool isValid = viewModel.SetExpression(args.ExpressionText, out var error);
                args.IsValid = isValid;
                args.Error = error;
            };

            flyout.ShowAt(this, true);
        }
    }

    private void RemoveExpression_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is BaseEditorViewModel { IsDisposed: false, CanEditProperty.Value: true } viewModel)
        {
            viewModel.RemoveExpression();
        }
    }

    private async void CopyPropertyPath_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is BaseEditorViewModel { IsDisposed: false } viewModel
            && viewModel.PropertyAdapter.GetEngineProperty() is { } engineProperty
            && engineProperty.GetOwnerObject() is { } engineObject
            && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            string propertyPath = $"{{{engineObject.Id}}}.{engineProperty.Name}";
            await clipboard.SetTextAsync(propertyPath);
        }
    }

    private async void CopyGetPropertyCode_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is BaseEditorViewModel { IsDisposed: false } viewModel
            && viewModel.PropertyAdapter.GetEngineProperty() is { } engineProperty
            && engineProperty.GetOwnerObject() is { } engineObject
            && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            string typeName = GetTypeAlias(engineProperty.ValueType);
            string code = $"GetProperty<{typeName}>(\"{{{engineObject.Id}}}.{engineProperty.Name}\")";
            await clipboard.SetTextAsync(code);
        }
    }

    private static string GetTypeAlias(Type type)
    {
        return CSharpTypeKeywords.TryGetCSharpKeyword(type, out string? keyword)
            ? keyword
            : type.FullName ?? type.Name;
    }
}
