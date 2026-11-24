using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Beutl.Animation;
using Beutl.Configuration;
using Beutl.Controls;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.GraphEditorTab.ViewModels;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.PropertyEditors;
using Beutl.Editor.Services;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Proxy;
using Beutl.ProjectSystem;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using ExtensionGuard = Beutl.Api.Services.ExtensionGuard;

namespace Beutl.Editor.Components.PropertyEditors.Views;

public sealed partial class PropertyEditorMenu : UserControl
{
    private IDisposable? _componentSubscription;
    private ExpressionEditorFlyout? _expressionFlyout;
    private int _deinterlaceProbeVersion;

    internal bool EditComponentsInFlyout { get; init; }

    public PropertyEditorMenu()
    {
        InitializeComponent();
        Bind(ToolTip.TipProperty, this.GetObservable(DataContextProperty)
            .Select(v => v is PropertyEditorViewModel viewModel
                ? viewModel.HasAnimation
                    .CombineLatest(viewModel.HasExpression)
                    .Select(t => t switch
                    {
                        (true, true) =>
                            $"- {MessageStrings.RightClickToShowMenu}\n- {MessageStrings.AnimationIsEnabled}\n- {MessageStrings.ExpressionIsSet}",
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
        transferInterpretationMenu.IsVisible = DataContext is VideoSourceEditorViewModel { HasTransferInterpretation: true };
        deinterlaceMenu.IsVisible = DataContext is VideoSourceEditorViewModel { HasDeinterlace: true };
        _deinterlaceProbeVersion++;
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

    private async void Menu_Opening(object? sender, EventArgs e)
    {
        int probeVersion = ++_deinterlaceProbeVersion;
        deinterlaceStatusItem.IsVisible = false;
        if (DataContext is not VideoSourceEditorViewModel { HasTransferInterpretation: true } model)
            return;

        SetCheckedItem(transferInterpretationMenu, model.SelectedTransferInterpretation);
        SetCheckedItem(deinterlaceMenu, model.SelectedDeinterlaceMode);
        SetCheckedItem(deinterlaceFieldOrderMenu, model.SelectedDeinterlaceFieldOrder);
        SetCheckedItem(deinterlaceMethodMenu, model.SelectedDeinterlaceMethod);
        SetCheckedItem(deinterlaceFrameRateMenu, model.SelectedDeinterlaceFrameRate);
        bool canSetOptions = model.SelectedDeinterlaceMode != DeinterlaceMode.Off;
        deinterlaceFieldOrderMenu.IsEnabled = canSetOptions;
        deinterlaceMethodMenu.IsEnabled = canSetOptions;
        deinterlaceFrameRateMenu.IsEnabled = canSetOptions;

        var decoders = ExtensionGuard.FindService<DecoderRegistry>(model) ?? ControlDecoders.GetDecoders(this);
        if (decoders == null) return;

        var config = ExtensionGuard.FindService<GlobalConfiguration>(model);
        bool preferProxy = config?.EditorConfig.PreviewSourceMode.Value == PreviewSourceMode.PreferProxy;
        var preset = (ProxyPreset)(config?.ProxyStoreConfig.DefaultPreset.Value ?? (int)ProxyPreset.Quarter);
        if (!Enum.IsDefined(preset)) preset = ProxyPreset.Quarter;

        var info = await model.ProbeDeinterlaceStatusAsync(decoders, preferProxy, preset);
        if (probeVersion != _deinterlaceProbeVersion || sender is not FAMenuFlyout { IsOpen: true } || info == null)
            return;

        string? status = FormatDeinterlaceStatus(info);
        deinterlaceStatusItem.Text = status;
        deinterlaceStatusItem.IsVisible = status != null;
    }

    private static void SetCheckedItem<T>(FAMenuFlyoutSubItem menu, T? selected) where T : struct, Enum
    {
        foreach (var item in menu.Items.OfType<FARadioMenuFlyoutItem>())
            item.IsChecked = item.Tag is T value && selected is { } choice && EqualityComparer<T>.Default.Equals(choice, value);
    }

    private static string? FormatDeinterlaceStatus(VideoStreamInfo info)
    {
        string? order = info.DetectedFieldOrder switch
        {
            VideoFieldOrder.Progressive => GraphicsStrings.VideoDeinterlace_Progressive,
            VideoFieldOrder.TopFieldFirst => GraphicsStrings.VideoDeinterlace_TopFieldFirst,
            VideoFieldOrder.BottomFieldFirst => GraphicsStrings.VideoDeinterlace_BottomFieldFirst,
            VideoFieldOrder.Mixed => GraphicsStrings.VideoDeinterlace_Mixed,
            _ => null,
        };
        return info.DeinterlaceStatus switch
        {
            DeinterlaceApplicationStatus.Applied when order != null => $"{order} · {GraphicsStrings.VideoDeinterlace_Applied}",
            DeinterlaceApplicationStatus.Applied => GraphicsStrings.VideoDeinterlace_Applied,
            DeinterlaceApplicationStatus.NotNeededProgressive => GraphicsStrings.VideoDeinterlace_ProgressiveNoProcessing,
            DeinterlaceApplicationStatus.UnsupportedByBackend => GraphicsStrings.VideoDeinterlace_Unsupported,
            _ => order,
        };
    }

    private void DeinterlaceOption_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is FARadioMenuFlyoutItem item && DataContext is VideoSourceEditorViewModel model)
            model.SetDeinterlaceOption(item.Tag);
    }

    private void TransferInterpretation_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is FARadioMenuFlyoutItem { Tag: VideoTransferInterpretation value }
            && DataContext is VideoSourceEditorViewModel model)
        {
            model.SetTransferInterpretation(value);
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
        // The inline view lies on the timeline track of one element, which the copy that stands for several has none of.
        editInlineAnimationItem.IsVisible = supportsAnimation && model?.EditsSeveralElements != true;
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
        if (DataContext is not PropertyEditorViewModel model) return;

        model.SeparateComponents();
        if (EditComponentsInFlyout)
            Dispatcher.UIThread.Post(() => ShowComponentEditors(model));
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

            if (EditComponentsInFlyout && viewModel.SeparatedComponents.Value is not null)
            {
                ShowComponentEditors(viewModel);
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
            else if (!viewModel.HasAnimation.Value && viewModel.HasExpression.Value && SupportsTextExpression(viewModel))
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
            else if (sender is Button button)
            {
                button.ContextFlyout?.ShowAt(button);
            }
        }
    }

    private void ShowComponentEditors(PropertyEditorViewModel model)
    {
        if (model.IsDisposed || !ReferenceEquals(DataContext, model) || !this.IsAttachedToVisualTree()
            || model.SeparatedComponents.Value is null) return;

        var editors = new ComponentEditors(model, combined: null) { MinWidth = 240 };
        var flyout = new Flyout { Content = editors, Placement = PlacementMode.BottomEdgeAlignedRight };
        IDisposable closeWhenCombined = model.SeparatedComponents.Where(components => components is null)
            .Subscribe(_ => flyout.Hide());
        flyout.Closed += (_, _) =>
        {
            closeWhenCombined.Dispose();
            editors.Dispose();
        };
        flyout.ShowAt(this);
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
                if (string.IsNullOrWhiteSpace(args.ExpressionText))
                {
                    if (viewModel.HasExpression.Value)
                    {
                        viewModel.RemoveExpression();
                    }

                    return;
                }

                args.IsValid = viewModel.SetExpression(args.ExpressionText, out var error);
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
