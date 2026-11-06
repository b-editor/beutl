using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Beutl.Animation;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.GraphEditorTab.ViewModels;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.PropertyEditors;
using Beutl.Editor.Services;
using Beutl.ProjectSystem;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Editor.Components.PropertyEditors.Views;

public sealed partial class PropertyEditorMenu : UserControl
{
    private IDisposable? _componentSubscription;
    private ExpressionEditorFlyout? _expressionFlyout;

    public PropertyEditorMenu()
    {
        InitializeComponent();
        Bind(ToolTip.TipProperty, this.GetObservable(DataContextProperty)
            .Select(v => v is PropertyEditorViewModel viewModel
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
        FillCopySubItem(copyPropertyPathSubItem, CopyPropertyPath_Click);
        FillCopySubItem(copyGetPropertyCodeSubItem, CopyGetPropertyCode_Click);

        _componentSubscription?.Dispose();
        _componentSubscription = null;
        if (DataContext is PropertyEditorViewModel model)
        {
            _componentSubscription = model.SeparatedProperties.CombineLatest(model.HasExpression)
                .Subscribe(state => UpdateActionVisibility(model, state.First != null));
            model.Own(_componentSubscription);
        }
        else
        {
            UpdateActionVisibility(null, false);
        }
    }

    // A path names one property, so a row that properties share asks which one. Each has a row of its own once separated.
    private void FillCopySubItem(FAMenuFlyoutSubItem subItem, EventHandler<RoutedEventArgs> click)
    {
        subItem.Items.Clear();
        if (DataContext is PropertyEditorViewModel { EditedProperties: { Count: > 1 } properties })
        {
            foreach (IPropertyAdapter property in properties)
            {
                var item = new FAMenuFlyoutItem { Text = property.DisplayName, Tag = property };
                item.Click += click;
                subItem.Items.Add(item);
            }
        }
    }

    private void UpdateActionVisibility(PropertyEditorViewModel? model, bool separated)
    {
        bool supportsAnimation = model?.PropertyAdapter is IAnimatablePropertyAdapter && !separated;
        editAnimationItem.IsVisible = supportsAnimation;
        editInlineAnimationItem.IsVisible = supportsAnimation;
        removeAnimationItem.IsVisible = supportsAnimation;
        separateComponentsItem.IsVisible = model?.CanSeparateComponents == true && !separated;
        // The item's state and command are those of the editor it unseparates, which can be the one this one came from.
        PropertyEditorViewModel? unseparated = separated ? model : model?.SeparatedFrom;
        unseparateComponentsItem.IsVisible = unseparated != null;
        unseparateComponentsItem.DataContext = unseparated;
        uniformEditorToggle.IsVisible = model is IConfigureUniformEditor && !separated;

        bool supportsExpression = model != null && !separated && SupportsTextExpression(model);
        editExpressionItem.IsVisible = supportsExpression;
        // Separation keeps an existing aggregate expression; let users remove it without discarding the tracks.
        removeExpressionItem.IsVisible = supportsExpression || separated && model?.HasExpression.Value == true;
        expressionSeparator.IsVisible = editExpressionItem.IsVisible || removeExpressionItem.IsVisible;

        bool isEngineProperty = model?.PropertyAdapter.GetEngineProperty() != null;
        bool asksWhichProperty = model?.EditedProperties.Count > 1 && !separated;
        copyPropertyPathItem.IsVisible = isEngineProperty;
        copyGetPropertyCodeItem.IsVisible = isEngineProperty;
        copyPropertyPathSubItem.IsVisible = asksWhichProperty;
        copyGetPropertyCodeSubItem.IsVisible = asksWhichProperty;
        copyPropertyPathSeparator.IsVisible = isEngineProperty || asksWhichProperty;
    }

    private void SeparateComponents_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PropertyEditorViewModel model) model.SeparateComponents();
    }

    private void UnseparateComponents_Click(object? sender, RoutedEventArgs e)
    {
        if (unseparateComponentsItem.DataContext is PropertyEditorViewModel model) model.UnseparateComponents();
    }

    private void Button_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PropertyEditorViewModel { IsDisposed: false } viewModel)
        {
            if (!viewModel.CanEditProperty.Value)
            {
                // Keep the copy actions available on a locked clip.
                if (sender is Button readOnlyButton)
                    readOnlyButton.ContextFlyout?.ShowAt(readOnlyButton);
                return;
            }

            if (viewModel.HasExpression.Value && SupportsTextExpression(viewModel))
            {
                // The editor does not light-dismiss, so a second click on the button closes it and drops
                // the unconfirmed edit, as × does.
                if (_expressionFlyout is { IsOpen: true })
                {
                    _expressionFlyout.Hide();
                }
                else
                {
                    EditExpression_Click(sender, e);
                }
            }
            else if (viewModel.HasAnimation.Value && viewModel.GetService<IEditorClock>() is { } editorClock)
            {
                MediaTime keyTime = editorClock.CurrentTime.Value;
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
        if (DataContext is PropertyEditorViewModel { IsDisposed: false, CanEditProperty.Value: true } viewModel
            && viewModel.PropertyAdapter is IAnimatablePropertyAdapter animatableProperty
            && viewModel.GetService<IEditorContext>() is { } editorContext)
        {
            viewModel.PrepareToEditAnimation();

            // Shows the animation in a graph editor tab.
            Element? element = viewModel.GetService<Element>();
            var animation = animatableProperty.Animation as IKeyFrameAnimation;
            GraphEditorTabViewModel graphEditorTab =
                GraphEditorTabViewModel.FindReusable(editorContext, element, animation)
                ?? new GraphEditorTabViewModel(editorContext);
            graphEditorTab.Element.Value = element;
            graphEditorTab.Select(animation);
            editorContext.OpenToolTab(graphEditorTab);
        }
    }

    private void RemoveAnimation_Click(object? sender, RoutedEventArgs e)
    {
        // Also a repair on a recovery-protected element, so it follows CanRemoveAnimation.
        if (DataContext is PropertyEditorViewModel { IsDisposed: false, CanRemoveAnimation.Value: true } viewModel)
        {
            viewModel.RemoveAnimation();
        }
    }

    private void EditInlineAnimation_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PropertyEditorViewModel { IsDisposed: false, CanEditProperty.Value: true } viewModel
            && viewModel.PropertyAdapter is IAnimatablePropertyAdapter animatableProperty
            && viewModel.GetService<IEditorContext>() is { } editorContext
            && viewModel.GetService<Element>() is { } element)
        {
            viewModel.PrepareToEditAnimation();

            if (animatableProperty.Animation is IKeyFrameAnimation
                && viewModel.InlineProperty is { } inlineProperty
                && FindOrOpenTimeline(editorContext) is { } timeline)
            {
                timeline.AttachInline(inlineProperty, element);
            }
        }
    }

    // The inline view lives in the timeline, so a closed timeline is opened first, the way
    // "Edit animation" opens its graph editor tab.
    private static TimelineTabViewModel? FindOrOpenTimeline(IEditorContext editorContext)
    {
        if (editorContext.FindToolTab<TimelineTabViewModel>() is { } timeline)
            return timeline;

        var created = new TimelineTabViewModel(editorContext);
        if (editorContext.OpenToolTab(created))
            return created;

        created.Dispose();
        return null;
    }

    private void EditExpression_Click(object? sender, RoutedEventArgs e)
    {
        // Opening the editor again would stack a second one over it; the open one keeps its edit.
        if (_expressionFlyout is { IsOpen: true }) return;

        if (DataContext is PropertyEditorViewModel { IsDisposed: false, CanEditProperty.Value: true } viewModel
            && SupportsTextExpression(viewModel))
        {
            string? currentExpression = viewModel.GetExpressionString();

            var flyout = new ExpressionEditorFlyout();
            flyout.Placement = PlacementMode.BottomEdgeAlignedRight;
            flyout.ExpressionText = currentExpression ?? "";
            flyout.ErrorMessage = viewModel.ExpressionError;
            if (viewModel.ExpressionExample is { } example)
            {
                flyout.ExpressionHint = string.Format(Strings.Expression_ReturnsBothValues, example);
                flyout.ExpressionPlaceholder = example;
            }
            flyout.Confirmed += (_, args) =>
            {
                bool isValid = viewModel.SetExpression(args.ExpressionText, out var error);
                args.IsValid = isValid;
                args.Error = error;
            };

            _expressionFlyout = flyout;
            flyout.ShowAt(this);
        }
    }

    private static bool SupportsTextExpression(PropertyEditorViewModel viewModel)
        => viewModel.SeparatedComponents.Value == null
           && viewModel.PropertyAdapter is IExpressionPropertyAdapter
           && viewModel.PropertyAdapter.GetEngineProperty() is not IExpressionProperty { IsReference: true };

    private void RemoveExpression_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PropertyEditorViewModel { IsDisposed: false, CanEditProperty.Value: true } viewModel)
        {
            viewModel.RemoveExpression();
        }
    }

    private async void CopyPropertyPath_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PropertyEditorViewModel { IsDisposed: false } viewModel
            && PropertyToCopy(sender, viewModel).GetEngineProperty() is { } engineProperty
            && engineProperty.GetOwnerObject() is { } engineObject
            && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            string propertyPath = $"{{{engineObject.Id.Value}}}.{engineProperty.Name}";
            await clipboard.SetTextAsync(propertyPath);
        }
    }

    private async void CopyGetPropertyCode_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PropertyEditorViewModel { IsDisposed: false } viewModel
            && PropertyToCopy(sender, viewModel).GetEngineProperty() is { } engineProperty
            && engineProperty.GetOwnerObject() is { } engineObject
            && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            string typeName = GetTypeAlias(engineProperty.ValueType);
            string code = $"GetProperty<{typeName}>(\"{{{engineObject.Id.Value}}}.{engineProperty.Name}\")";
            await clipboard.SetTextAsync(code);
        }
    }

    private static IPropertyAdapter PropertyToCopy(object? sender, PropertyEditorViewModel viewModel)
        => (sender as Control)?.Tag as IPropertyAdapter ?? viewModel.PropertyAdapter;

    // The type as C# code names it without usings: keywords, "Outer.Inner" for a nested type and the type
    // arguments in angle brackets, where Type.FullName gives "Outer+Inner" and "List`1[[...]]".
    internal static string GetTypeAlias(Type type)
    {
        if (Nullable.GetUnderlyingType(type) is { } underlying)
            return $"{GetTypeAlias(underlying)}?";
        if (type.IsArray)
            return $"{GetTypeAlias(type.GetElementType()!)}[{new string(',', type.GetArrayRank() - 1)}]";
        if (CSharpTypeKeywords.TryGetCSharpKeyword(type, out string? keyword))
            return keyword;

        string name = type.Name;
        int tick = name.IndexOf('`');
        if (tick >= 0)
            name = $"{name[..tick]}<{string.Join(", ", type.GetGenericArguments().Select(GetTypeAlias))}>";

        if (type.IsNested)
            return $"{GetTypeAlias(type.DeclaringType!)}.{name}";
        return type.Namespace is { } ns ? $"{ns}.{name}" : name;
    }
}
