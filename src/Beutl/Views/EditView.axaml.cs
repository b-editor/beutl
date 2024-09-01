using System.Collections.Specialized;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using Beutl.Controls;
using Beutl.ExceptionHandler;
using Beutl.Logging;
using Beutl.Services;
using Beutl.ViewModels;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;
using ReDocking;

namespace Beutl.Views;

public sealed partial class EditView : UserControl
{
    private readonly ILogger _logger = Log.CreateLogger<EditView>();

    public EditView()
    {
        InitializeComponent();

        this.GetObservable(IsKeyboardFocusWithinProperty)
            .Subscribe(v => Player.Player.SetSeekBarOpacity(v ? 1 : 0.8));
    }

    private void OnTabViewSelectedItemChanged(object? obj)
    {
        if (obj is BcTabItem { DataContext: ToolTabViewModel { Context.Extension.Name: string name } })
        {
            _logger.LogInformation("'{ToolTabName}' has been selected.", name);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (DataContext is EditViewModel viewModel)
        {
            // TextBox.OnKeyDown で e.Handled が True に設定されないので
            if (e.Key == Key.Space && e.Source is TextBox)
            {
                return;
            }

            // KeyBindingsは変更してはならない。
            foreach (KeyBinding binding in viewModel.KeyBindings)
            {
                if (e.Handled)
                    break;
                binding.TryHandle(e);
            }
        }
    }

    private void OnSideBarButtonDisplayModeChanged(object? sender, SideBarButtonDisplayModeChangedEventArgs e)
    {
    }

    private static ReactiveCollection<ToolTabViewModel> GetItemsSource(EditViewModel viewModel,
        DockAreaLocation location)
    {
        return location switch
        {
            DockAreaLocation.Left => viewModel.LeftTools,
            DockAreaLocation.Right => viewModel.RightTools,
            DockAreaLocation.TopLeft => viewModel.LeftTopTools,
            DockAreaLocation.BottomLeft => viewModel.LeftBottomTools,
            DockAreaLocation.TopRight => viewModel.RightTopTools,
            DockAreaLocation.BottomRight => viewModel.RightBottomTools,
            _ => throw new ArgumentOutOfRangeException(nameof(location), location, null)
        };
    }

    private static ReactiveProperty<ToolTabViewModel?> GetSelectedItem(EditViewModel viewModel,
        DockAreaLocation location)
    {
        return location switch
        {
            DockAreaLocation.Left => viewModel.SelectedLeftTool,
            DockAreaLocation.Right => viewModel.SelectedRightTool,
            DockAreaLocation.TopLeft => viewModel.SelectedLeftTopTool,
            DockAreaLocation.BottomLeft => viewModel.SelectedLeftBottomTool,
            DockAreaLocation.TopRight => viewModel.SelectedRightTopTool,
            DockAreaLocation.BottomRight => viewModel.SelectedRightBottomTool,
            _ => throw new ArgumentOutOfRangeException(nameof(location), location, null)
        };
    }

    private void OnSideBarButtonDrop(object? sender, SideBarButtonMoveEventArgs e)
    {
        if (DataContext is not EditViewModel viewModel) return;
        var oldItems = GetItemsSource(viewModel, e.SourceLocation);
        var oldSelectedItem = GetSelectedItem(viewModel, e.SourceLocation);
        var newItems = GetItemsSource(viewModel, e.DestinationLocation);

        if (e.Item is not ToolTabViewModel item)
        {
            return;
        }

        if (oldSelectedItem.Value == item)
        {
            oldSelectedItem.Value = null;
        }

        if (oldItems == newItems)
        {
            var sourceIndex = oldItems.IndexOf(item);
            var destinationIndex = e.DestinationIndex;
            if (sourceIndex < destinationIndex)
            {
                destinationIndex--;
            }

            oldItems.Move(sourceIndex, destinationIndex);
            item.Context.IsSelected.Value = true;
        }
        else
        {
            oldItems.Remove(item);
            var newItem = item;
            // var newItem = new ToolTabViewModel(item.Context, viewModel);
            newItems.Insert(e.DestinationIndex, newItem);
            newItem.Context.IsSelected.Value = true;
            newItem.Context.Placement.Value = e.DestinationLocation switch
            {
                DockAreaLocation.Left => ToolTabExtension.TabPlacement.Left,
                DockAreaLocation.Right => ToolTabExtension.TabPlacement.Right,
                DockAreaLocation.TopLeft => ToolTabExtension.TabPlacement.TopLeft,
                DockAreaLocation.BottomLeft => ToolTabExtension.TabPlacement.BottomLeft,
                DockAreaLocation.TopRight => ToolTabExtension.TabPlacement.TopRight,
                DockAreaLocation.BottomRight => ToolTabExtension.TabPlacement.BottomRight,
                _ => ToolTabExtension.TabPlacement.Left
            };
        }

        e.Handled = true;
    }
}
