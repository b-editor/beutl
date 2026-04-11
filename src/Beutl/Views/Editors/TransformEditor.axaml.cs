using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.Views;
using Beutl.Graphics.Transformation;
using Beutl.Models;
using Beutl.Services;
using Beutl.ViewModels.Editors;
using FluentAvalonia.UI.Controls;

namespace Beutl.Views.Editors;

public partial class TransformEditor : UserControl
{
    private static readonly CrossFade s_transition = new(TimeSpan.FromMilliseconds(250));
    private CancellationTokenSource? _lastTransitionCts;
    private FallbackObjectView? _fallbackObjectView;

    private static FAMenuFlyout? s_flyout;
    private static EventHandler<RoutedEventArgs>? s_handler;

    public TransformEditor()
    {
        InitializeComponent();
        expandToggle.GetObservable(ToggleButton.IsCheckedProperty)
            .Subscribe(async v =>
            {
                _lastTransitionCts?.Cancel();
                _lastTransitionCts = new CancellationTokenSource();
                CancellationToken localToken = _lastTransitionCts.Token;

                if (v == true)
                {
                    await s_transition.Start(null, content, localToken);
                }
                else
                {
                    await s_transition.Start(content, null, localToken);
                }
            });

        ChangeTypeMenu.ItemsSource = CreateMenuItems(TransformTypeClicked);
        PresenterChangeTypeMenu.ItemsSource = CreateMenuItems(TransformTypeClicked);

        this.GetObservable(DataContextProperty)
            .Select(x => x as TransformEditorViewModel)
            .Select(x => x?.IsFallback.Select(_ => x) ?? Observable.ReturnThenNever<TransformEditorViewModel?>(null))
            .Switch()
            .Where(v => v?.IsFallback.Value == true)
            .Take(1)
            .Subscribe(_ =>
            {
                _fallbackObjectView = new FallbackObjectView();
                content.Children.Add(_fallbackObjectView);
            });

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, DragOver);
        AddHandler(DragDrop.DropEvent, Drop);
    }

    private void Drop(object? sender, DragEventArgs e)
    {
        KnownTransformType ToKnownType(Type type)
        {
            if (type == typeof(TransformGroup))
                return KnownTransformType.Group;
            else if (type == typeof(TranslateTransform))
                return KnownTransformType.Translate;
            else if (type == typeof(RotationTransform))
                return KnownTransformType.Rotation;
            else if (type == typeof(ScaleTransform))
                return KnownTransformType.Scale;
            else if (type == typeof(SkewTransform))
                return KnownTransformType.Skew;
            else if (type == typeof(Rotation3DTransform))
                return KnownTransformType.Rotation3D;
            else
                return KnownTransformType.Unknown;
        }

        if (DataContext is not TransformEditorViewModel { IsDisposed: false } viewModel) return;
        if (e.DataTransfer.TryGetValue(BeutlDataFormats.Transform) is not { } data) return;

        if (CoreObjectClipboard.IsJsonData(data))
        {
            if (viewModel.TryPasteJson(data))
            {
                e.Handled = true;
            }
            return;
        }

        if (TypeFormat.ToType(data) is { } type)
        {
            KnownTransformType knownType = ToKnownType(type);
            if (knownType == KnownTransformType.Unknown)
                return;

            if (viewModel.IsGroup.Value)
            {
                viewModel.AddItem(knownType);
            }
            else
            {
                viewModel.ChangeType(knownType);
            }

            e.Handled = true;
        }
    }

    private void DragOver(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.Contains(BeutlDataFormats.Transform))
        {
            e.DragEffects = DragDropEffects.Copy | DragDropEffects.Link;
            e.Handled = true;
        }
    }

    private static MenuFlyoutItem[] CreateMenuItems(EventHandler<RoutedEventArgs>? handler)
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
                var obj = new MenuFlyoutItem() { Tag = x.Tag, Text = x.Name };
                if (handler != null)
                {
                    obj.Click += handler;
                }

                if (x.Icon != null)
                {
                    obj.IconSource = new PathIconSource
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
            Placement = PlacementMode.BottomEdgeAlignedRight,
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
        if (sender is MenuFlyoutItem { Tag: KnownTransformType type }
            && DataContext is TransformEditorViewModel { IsDisposed: false } viewModel)
        {
            viewModel.AddItem(type);
        }
    }

    private void TransformTypeClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: KnownTransformType type }
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

    private async void CopyClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not BaseEditorViewModel { IsDisposed: false } vm) return;
        try
        {
            await vm.CopyAsync();
        }
        catch (Exception ex)
        {
            NotificationService.ShowError(Strings.Error, ex.Message);
        }
    }

    private async void PasteClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not BaseEditorViewModel { IsDisposed: false } vm) return;
        try
        {
            if (!await vm.PasteAsync())
            {
                NotificationService.ShowInformation(Strings.Paste, MessageStrings.CannotPasteFromClipboard);
            }
        }
        catch (Exception ex)
        {
            NotificationService.ShowError(Strings.Error, ex.Message);
        }
    }

    private async void CopyPasteFlyout_Opening(object? sender, EventArgs e)
    {
        if (DataContext is BaseEditorViewModel { IsDisposed: false } vm)
        {
            await vm.RefreshCanPasteAsync();
        }
    }

    private async void SelectTarget_Requested(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not TransformEditorViewModel { IsDisposed: false } vm) return;

        var targets = vm.GetAvailableTargets();
        var pickerVm = new TargetPickerFlyoutViewModel();
        pickerVm.Initialize(targets);

        var flyout = new TargetPickerFlyout(pickerVm);
        flyout.ShowAt(this);

        var tcs = new TaskCompletionSource<Transform?>();
        flyout.Dismissed += (_, _) => tcs.TrySetResult(null);
        flyout.Confirmed += (_, _) => tcs.TrySetResult(
            (pickerVm.SelectedItem.Value?.UserData as TargetObjectInfo)?.Object as Transform);

        var result = await tcs.Task;
        if (result != null)
        {
            vm.SetTarget(result);
        }
    }
}
