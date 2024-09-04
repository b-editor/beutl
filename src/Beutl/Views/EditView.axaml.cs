using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Beutl.Controls;
using Beutl.Logging;
using Beutl.ViewModels;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;
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

    private ReactiveCollection<ToolTabViewModel> GetItemsSource(DockAreaLocation location)
    {
        var sideBar = DockHost.DockAreas.FirstOrDefault(i => i.SideBar?.Location == location.LeftRight)?.SideBar;

        if (sideBar == null)
        {
            throw new InvalidOperationException("SideBar not found.");
        }

        return location.ButtonLocation switch
        {
            SideBarButtonLocation.UpperTop => sideBar.UpperTopToolsSource,
            SideBarButtonLocation.UpperBottom => sideBar.UpperBottomToolsSource,
            SideBarButtonLocation.LowerTop => sideBar.LowerTopToolsSource,
            SideBarButtonLocation.LowerBottom => sideBar.LowerBottomToolsSource,
            _ => throw new ArgumentOutOfRangeException(nameof(location), location, null)
        } as ReactiveCollection<ToolTabViewModel> ?? throw new InvalidOperationException();
    }

    private static ToolTabExtension.TabPlacement ToTabPlacementEnum(DockAreaLocation location)
    {
        return (location.ButtonLocation, location.LeftRight) switch
        {
            (SideBarButtonLocation.UpperTop, SideBarLocation.Left) =>
                ToolTabExtension.TabPlacement.LeftUpperTop,
            (SideBarButtonLocation.UpperBottom, SideBarLocation.Left) =>
                ToolTabExtension.TabPlacement.LeftUpperBottom,
            (SideBarButtonLocation.LowerTop, SideBarLocation.Left) =>
                ToolTabExtension.TabPlacement.LeftLowerTop,
            (SideBarButtonLocation.LowerBottom, SideBarLocation.Left) =>
                ToolTabExtension.TabPlacement.LeftLowerBottom,
            (SideBarButtonLocation.UpperTop, SideBarLocation.Right) =>
                ToolTabExtension.TabPlacement.RightUpperTop,
            (SideBarButtonLocation.UpperBottom, SideBarLocation.Right) =>
                ToolTabExtension.TabPlacement.RightUpperBottom,
            (SideBarButtonLocation.LowerTop, SideBarLocation.Right) =>
                ToolTabExtension.TabPlacement.RightLowerTop,
            (SideBarButtonLocation.LowerBottom, SideBarLocation.Right) =>
                ToolTabExtension.TabPlacement.RightLowerBottom,
            _ => throw new ArgumentOutOfRangeException(nameof(location), location, null)
        };
    }

    private static ReactiveProperty<ToolTabViewModel?> GetSelectedItem(EditViewModel viewModel,
        DockAreaLocation location)
    {
        return (location.ButtonLocation, location.LeftRight) switch
        {
            (SideBarButtonLocation.UpperTop, SideBarLocation.Left) => viewModel.SelectedLeftUpperTopTool,
            (SideBarButtonLocation.UpperBottom, SideBarLocation.Left) => viewModel.SelectedLeftUpperBottomTool,
            (SideBarButtonLocation.LowerTop, SideBarLocation.Left) => viewModel.SelectedLeftLowerTopTool,
            (SideBarButtonLocation.LowerBottom, SideBarLocation.Left) => viewModel.SelectedLeftLowerBottomTool,
            (SideBarButtonLocation.UpperTop, SideBarLocation.Right) => viewModel.SelectedRightUpperTopTool,
            (SideBarButtonLocation.UpperBottom, SideBarLocation.Right) => viewModel.SelectedRightUpperBottomTool,
            (SideBarButtonLocation.LowerTop, SideBarLocation.Right) => viewModel.SelectedRightLowerTopTool,
            (SideBarButtonLocation.LowerBottom, SideBarLocation.Right) => viewModel.SelectedRightLowerBottomTool,
            _ => throw new ArgumentOutOfRangeException(nameof(location), location, null)
        };
    }

    private void OnSideBarButtonDrop(object? sender, SideBarButtonMoveEventArgs e)
    {
        if (DataContext is not EditViewModel viewModel) return;
        var oldItems = GetItemsSource(e.SourceLocation);
        var oldSelectedItem = GetSelectedItem(viewModel, e.SourceLocation);
        var newItems = GetItemsSource(e.DestinationLocation);

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
            newItem.Context.Placement.Value = ToTabPlacementEnum(e.DestinationLocation);
        }

        e.Handled = true;
    }
}
