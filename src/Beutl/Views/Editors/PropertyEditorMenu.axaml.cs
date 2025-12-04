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

        // プロパティパスのコピーはEnginePropertyの場合のみ表示
        bool isEngineProperty = (DataContext as BaseEditorViewModel)?.PropertyAdapter.GetEngineProperty() != null;
        copyPropertyPathSeparator.IsVisible = isEngineProperty;
        copyPropertyPathItem.IsVisible = isEngineProperty;
        copyGetPropertyCodeItem.IsVisible = isEngineProperty;
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
            flyout.ExpressionText = currentExpression ?? "";
            flyout.Validator = expression =>
            {
                bool isValid = viewModel.ValidateExpression(expression, out var error);
                return (isValid, error);
            };
            flyout.Confirmed += (_, _) =>
            {
                if (!string.IsNullOrWhiteSpace(flyout.ExpressionText))
                {
                    viewModel.SetExpression(flyout.ExpressionText);
                }
            };

            flyout.ShowAt(this);
        }
    }

    private void RemoveExpression_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is BaseEditorViewModel { IsDisposed: false } viewModel)
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
        // プリミティブ型のエイリアス
        return type switch
        {
            _ when type == typeof(bool) => "bool",
            _ when type == typeof(byte) => "byte",
            _ when type == typeof(sbyte) => "sbyte",
            _ when type == typeof(char) => "char",
            _ when type == typeof(short) => "short",
            _ when type == typeof(ushort) => "ushort",
            _ when type == typeof(int) => "int",
            _ when type == typeof(uint) => "uint",
            _ when type == typeof(long) => "long",
            _ when type == typeof(ulong) => "ulong",
            _ when type == typeof(float) => "float",
            _ when type == typeof(double) => "double",
            _ when type == typeof(decimal) => "decimal",
            _ when type == typeof(string) => "string",
            _ when type == typeof(object) => "object",
            _ => type.FullName ?? type.Name
        };
    }
}
