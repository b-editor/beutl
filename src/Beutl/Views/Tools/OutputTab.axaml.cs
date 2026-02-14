using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Beutl.Controls;
using Beutl.Services;
using Beutl.ViewModels.Dialogs;
using Beutl.ViewModels.Tools;
// using Beutl.Views.NodeTree;
using AddOutputProfileDialog = Beutl.Views.Dialogs.AddOutputProfileDialog;

namespace Beutl.Views.Tools;

public partial class OutputTab : UserControl
{
    private readonly IDataTemplate _sharedDataTemplate = new _DataTemplate();

    public OutputTab()
    {
        InitializeComponent();
        contentControl.ContentTemplate = _sharedDataTemplate;
    }

    private async void OnAddClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not OutputTabViewModel viewModel) return;

        var ext = OutputService.GetExtensions(viewModel.EditViewModel.Scene.GetType());
        if (ext.Length == 1)
        {
            viewModel.AddItem(ext[0]);
        }
        else
        {
            var dialogViewModel = new AddOutputProfileViewModel(viewModel);
            var dialog = new AddOutputProfileDialog { DataContext = dialogViewModel };

            await dialog.ShowAsync();
        }

        viewModel.Save();
    }

    private void OnRemoveClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not OutputTabViewModel viewModel) return;
        switch (sender)
        {
            case ICommandSource { CommandParameter: OutputProfileItem item }:
                viewModel.RemoveItem(item);
                viewModel.Save();
                break;
            case ICommandSource { CommandParameter: OutputPresetItem presetItem }:
                OutputPresetService.Instance.Items.Remove(presetItem);
                OutputPresetService.Instance.SaveItems();
                break;
        }
    }

    private void OnConvertPresetClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not ICommandSource { CommandParameter: OutputProfileItem item }) return;

        OutputPresetService.Instance.AddItem(item.Context, $"{item.Context.Name.Value} (Preset)");
        OutputPresetService.Instance.SaveItems();
    }

    private void OnRenameClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not OutputTabViewModel viewModel) return;
        OutputProfileItem? item = (sender as ICommandSource)?.CommandParameter as OutputProfileItem;
        OutputPresetItem? presetItem = (sender as ICommandSource)?.CommandParameter as OutputPresetItem;
        if (item == null && presetItem == null) return;
        var target = (sender as Control)?.Tag as Control ?? MoreButton;

        var flyout = new RenameFlyout { Text = item?.Context.Name.Value ?? presetItem?.Name.Value };

        flyout.Confirmed += (_, text) =>
        {
            if (item != null)
            {
                item.Context.Name.Value = text ?? "";
                viewModel.Save();
            }
            else if (presetItem != null)
            {
                presetItem.Name.Value = text ?? "";
                OutputPresetService.Instance.SaveItems();
            }
        };

        flyout.ShowAt(target);
    }

    private sealed class _DataTemplate : IDataTemplate
    {
        private readonly Dictionary<OutputExtension, Control> _contextToViewType = [];

        public Control? Build(object? param)
        {
            if (param is OutputProfileItem item)
            {
                if (_contextToViewType.TryGetValue(item.Context.Extension, out Control? control))
                {
                    control.DataContext = item.Context;
                    return control;
                }
                else if (item.Context.Extension.TryCreateControl(item.EditorContext, out control))
                {
                    _contextToViewType[item.Context.Extension] = control;
                    control.DataContext = item.Context;
                    return control;
                }
            }

            return null;
        }

        public bool Match(object? data)
        {
            return data is OutputProfileItem;
        }
    }

    private void OnProfilesButtonClick(object? sender, RoutedEventArgs e)
    {
        ProfilesPopup.IsOpen = !ProfilesPopup.IsOpen;
    }

    public void OnProfileItemClick(object? sender, TappedEventArgs e)
    {
        if ((e.Source as StyledElement)?.GetSelfAndLogicalAncestors().Any(i => i is Button) == true) return;
        if (e.Source is not StyledElement { DataContext: OutputProfileItem item }) return;
        if (DataContext is not OutputTabViewModel viewModel) return;

        viewModel.SelectedItem.Value = item;
        ProfilesPopup.IsOpen = false;
    }

    public void OnPresetItemClick(object? sender, TappedEventArgs e)
    {
        if ((e.Source as StyledElement)?.GetSelfAndLogicalAncestors().Any(i => i is Button) == true) return;
        if (e.Source is not StyledElement { DataContext: OutputPresetItem item }) return;
        if (DataContext is not OutputTabViewModel viewModel) return;

        if (viewModel.SelectedItem.Value?.Context is ISupportOutputPreset supportPreset)
            item.Apply(supportPreset);

        ProfilesPopup.IsOpen = false;
    }
}
