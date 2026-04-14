using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Beutl.Editor.Components.Helpers;
using Beutl.Graphics.Transformation;
using Beutl.Models;
using Beutl.Services;
using Beutl.ViewModels.Editors;
using FluentAvalonia.UI.Controls;

namespace Beutl.Views.Editors;

public partial class TransformEditor : UserControl
{
    private static FAMenuFlyout? s_flyout;
    private static EventHandler<RoutedEventArgs>? s_handler;
    private bool _flyoutOpen;

    public TransformEditor()
    {
        InitializeComponent();
        ExpandTransitionHelper.Attach(expandToggle, content);

        ChangeTypeMenu.ItemsSource = CreateMenuItems(TransformTypeClicked);
        PresenterChangeTypeMenu.ItemsSource = CreateMenuItems(TransformTypeClicked);

        FallbackObjectViewHelper.Attach(this, view => content.Children.Add(view));

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, DragOver);
        AddHandler(DragDrop.DropEvent, Drop);

        EditorMenuHelper.AttachCopyPasteAndTemplateMenus(
            this,
            (FAMenuFlyout)expandToggle.ContextFlyout!,
            (FAMenuFlyout)ReferenceMenuButton.Flyout!);
    }

    private void Drop(object? sender, DragEventArgs e)
    {
        if (DataContext is not TransformEditorViewModel { IsDisposed: false } viewModel) return;

        if (EditorDragDropHelper.TryHandleEditorDrop<Transform>(
                e,
                BeutlDataFormats.Transform,
                tryPasteJson: viewModel.TryPasteJson,
                onTemplateInstance: instance =>
                {
                    if (viewModel.IsGroup.Value)
                        viewModel.AddItem(instance);
                    else
                        viewModel.ChangeTransform(instance);
                },
                onTypePayload: type =>
                {
                    KnownTransformType knownType = ToKnownTransformType(type);
                    if (knownType == KnownTransformType.Unknown)
                        return false;

                    if (viewModel.IsGroup.Value)
                        viewModel.AddItem(knownType);
                    else
                        viewModel.ChangeType(knownType);
                    return true;
                }))
        {
            e.Handled = true;
        }
    }

    private static KnownTransformType ToKnownTransformType(Type type)
    {
        if (type == typeof(TransformGroup)) return KnownTransformType.Group;
        if (type == typeof(TranslateTransform)) return KnownTransformType.Translate;
        if (type == typeof(RotationTransform)) return KnownTransformType.Rotation;
        if (type == typeof(ScaleTransform)) return KnownTransformType.Scale;
        if (type == typeof(SkewTransform)) return KnownTransformType.Skew;
        if (type == typeof(Rotation3DTransform)) return KnownTransformType.Rotation3D;
        return KnownTransformType.Unknown;
    }

    private void DragOver(object? sender, DragEventArgs e)
    {
        EditorDragDropHelper.HandleEditorDragOver(e, BeutlDataFormats.Transform);
    }

    private static FAMenuFlyoutItem[] CreateMenuItems(EventHandler<RoutedEventArgs>? handler)
    {
        var items = new (KnownTransformType Tag, string Name, string? Icon)[]
        {
            (KnownTransformType.Group, GraphicsStrings.Group, null),
            (KnownTransformType.Translate, GraphicsStrings.TranslateTransform, "TranslateTransformIconData"),
            (KnownTransformType.Rotation, GraphicsStrings.Rotation, "RotationTransformIconData"),
            (KnownTransformType.Scale, GraphicsStrings.Scale, "ScaleTransformIconData"),
            (KnownTransformType.Skew, GraphicsStrings.SkewTransform, "SkewTransformIconData"),
            (KnownTransformType.Rotation3D, GraphicsStrings.Rotation3DTransform, "Rotation3DTransformIconData"),
            (KnownTransformType.Presenter, GraphicsStrings.Presenter, null)
        };
        return items.Select(x =>
            {
                var obj = new FAMenuFlyoutItem() { Tag = x.Tag, Text = x.Name };
                if (handler != null)
                {
                    obj.Click += handler;
                }

                if (x.Icon != null)
                {
                    obj.IconSource = new FAPathIconSource
                    {
                        Data = Application.Current?.FindResource(x.Icon) as Avalonia.Media.Geometry
                    };
                }

                return obj;
            })
            .ToArray();
    }

    private static FAMenuFlyout GetOrCreateFlyout()
    {
        return s_flyout ??= new FAMenuFlyout()
        {
            Placement = PlacementMode.Pointer,
            ItemsSource = CreateMenuItems((s, e) => s_handler?.Invoke(s, e))
        };
    }

    private void Tag_Click(object? sender, RoutedEventArgs e)
    {
        void Flyout_Closed(object? sender, EventArgs e)
        {
            if (sender is FAMenuFlyout flyout)
            {
                flyout.Closed -= Flyout_Closed;
                s_handler -= AddTransformClick;
            }
        }

        if (DataContext is TransformEditorViewModel { IsDisposed: false } viewModel)
        {
            if (viewModel.IsGroup.Value)
            {
                FAMenuFlyout flyout = GetOrCreateFlyout();
                flyout.ShowAt(expandToggle);

                s_handler += AddTransformClick;
                flyout.Closed += Flyout_Closed;
            }
            else
            {
                expandToggle.ContextFlyout?.ShowAt(expandToggle);
            }
        }
    }

    private void AddTransformClick(object? sender, RoutedEventArgs e)
    {
        if (sender is FAMenuFlyoutItem { Tag: KnownTransformType type }
            && DataContext is TransformEditorViewModel { IsDisposed: false } viewModel)
        {
            viewModel.AddItem(type);
        }
    }

    private void TransformTypeClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is FAMenuFlyoutItem { Tag: KnownTransformType type }
            && DataContext is TransformEditorViewModel { IsDisposed: false } viewModel)
        {
            viewModel.ChangeType(type);
        }
    }

    private void SetNullClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is TransformEditorViewModel { IsDisposed: false } viewModel)
        {
            viewModel.SetNull();
        }
    }

    private async void SelectTarget_Requested(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not TransformEditorViewModel { IsDisposed: false } vm) return;
        if (_flyoutOpen) return;

        try
        {
            _flyoutOpen = true;
            await TargetSelectionHelper.HandleSelectTargetRequestAsync<TransformEditorViewModel, Transform>(
                this,
                vm,
                vm => vm.GetAvailableTargets(),
                (vm, target) => vm.SetTarget(target));
        }
        finally
        {
            _flyoutOpen = false;
        }
    }
}
