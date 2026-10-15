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
                    KnownTransformType knownType = TransformEditorViewModel.FromDroppedType(type);
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

    private void DragOver(object? sender, DragEventArgs e)
    {
        EditorDragDropHelper.HandleEditorDragOver(e, BeutlDataFormats.Transform);
    }

    private static FAMenuFlyoutItem[] CreateMenuItems(EventHandler<RoutedEventArgs> handler)
    {
        return TransformEditorViewModel.KnownTransforms.Select(x =>
            {
                var obj = new FAMenuFlyoutItem() { Tag = x.Kind, Text = x.Name() };
                obj.Click += handler;

                if (x.IconKey != null)
                {
                    obj.IconSource = new FAPathIconSource
                    {
                        Data = Application.Current?.FindResource(x.IconKey) as Avalonia.Media.Geometry
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

        await PickerGuard.RunAsync(this, () =>
            TargetSelectionHelper.HandleSelectTargetRequestAsync<TransformEditorViewModel, Transform>(
                this,
                vm,
                vm => vm.GetAvailableTargets(),
                (vm, target) => vm.SetTarget(target)));
    }
}
