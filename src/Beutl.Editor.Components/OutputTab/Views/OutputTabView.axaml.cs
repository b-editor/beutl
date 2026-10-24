using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Beutl.Api.Services;
using Beutl.Controls;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.OutputTab.ViewModels;
using FluentAvalonia.UI.Controls;
using FluentIconSource = FluentIcons.Avalonia.Fluent.FluentIconSource;
using Icon = FluentIcons.Common.Icon;

namespace Beutl.Editor.Components.OutputTab.Views;

public partial class OutputTabView : UserControl
{
    private readonly IDataTemplate _sharedDataTemplate = new _DataTemplate();

    public OutputTabView()
    {
        InitializeComponent();
        contentControl.ContentTemplate = _sharedDataTemplate;
    }

    private async void OnAddClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not OutputTabViewModel viewModel) return;

        var ext = viewModel.GetExtensions(viewModel.Scene.GetType());
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
        if (sender is ICommandSource { CommandParameter: OutputProfileItem item })
        {
            RemoveAndSave(viewModel, item);
        }
    }

    private void OnConvertPresetClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not OutputTabViewModel viewModel) return;
        if (sender is not ICommandSource { CommandParameter: OutputProfileItem item }) return;

        ConvertToPreset(viewModel, item);
    }

    private void OnRenameClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not OutputTabViewModel) return;
        if ((sender as ICommandSource)?.CommandParameter is not OutputProfileItem item) return;

        ShowRenameFlyout(item, MoreButton);
    }

    private static void RemoveAndSave(OutputTabViewModel viewModel, OutputProfileItem profile)
    {
        viewModel.RemoveItem(profile);
        viewModel.Save();
    }

    private static void ConvertToPreset(OutputTabViewModel viewModel, OutputProfileItem profile)
    {
        viewModel.Presets.AddItem(profile.Context, $"{profile.Context.Name.Value} (Preset)");
        viewModel.Presets.SaveItems();
    }

    private sealed class _DataTemplate : IDataTemplate
    {
        public Control? Build(object? param)
        {
            if (param is not OutputProfileItem item)
                return null;

            var extension = item.Context.Extension;
            Control? control = ExtensionGuard.Call(extension, () => extension.TryCreateControl(
                item.EditorContext,
                item.Context,
                item,
                out Control? created) ? created : null);
            control?.DataContext = item.Context;
            return control;
        }

        public bool Match(object? data)
        {
            return data is OutputProfileItem;
        }
    }

    private void OnProfilesButtonClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not OutputTabViewModel viewModel) return;

        var pickerVm = new OutputPickerViewModel(viewModel.Items, viewModel.PresetItems);
        pickerVm.SetInitialSelection(viewModel.SelectedItem.Value);

        var flyout = new OutputPickerFlyout(pickerVm);
        flyout.Confirmed += OnPickerConfirmed;
        flyout.MoreMenuRequested += OnPickerMoreMenuRequested;
        flyout.Closed += (_, _) =>
        {
            flyout.Confirmed -= OnPickerConfirmed;
            flyout.MoreMenuRequested -= OnPickerMoreMenuRequested;
            pickerVm.Dispose();
        };

        flyout.ShowAt(ProfilesButton);
    }

    private void OnPickerConfirmed(OutputPickerFlyout sender, EventArgs e)
    {
        if (DataContext is not OutputTabViewModel viewModel) return;

        if (sender.ViewModel.ShowPresets.Value)
        {
            if (sender.ViewModel.SelectedPreset.Value?.UserData is OutputPresetItem preset
                && viewModel.SelectedItem.Value?.Context is ISupportOutputPreset supportPreset
                && preset.Extension.GetType() == ((IOutputContext)supportPreset).Extension.GetType())
            {
                preset.Apply(supportPreset);
            }
        }
        else
        {
            if (sender.ViewModel.SelectedProfile.Value?.UserData is OutputProfileItem profile)
            {
                viewModel.SelectedItem.Value = profile;
            }
        }
    }

    private void OnPickerMoreMenuRequested(OutputPickerFlyout sender, MoreMenuRequestedArgs args)
    {
        if (DataContext is not OutputTabViewModel viewModel) return;

        var menu = new FAMenuFlyout();

        switch (args.Item.UserData)
        {
            case OutputProfileItem profile:
                {
                    var removeItem = new FAMenuFlyoutItem
                    {
                        Text = Language.Strings.Remove,
                        IconSource = new FluentIconSource { Icon = Icon.Delete }
                    };
                    removeItem.Click += (_, _) => RemoveAndSave(viewModel, profile);
                    menu.Items.Add(removeItem);

                    var renameItem = new FAMenuFlyoutItem { Text = Language.Strings.Rename };
                    renameItem.Click += (_, _) => ShowRenameFlyout(profile, args.Anchor);
                    menu.Items.Add(renameItem);

                    var convertItem = new FAMenuFlyoutItem { Text = Language.Strings.Convert_to_preset };
                    convertItem.Click += (_, _) => ConvertToPreset(viewModel, profile);
                    menu.Items.Add(convertItem);
                    break;
                }
            case OutputPresetItem preset:
                {
                    var removeItem = new FAMenuFlyoutItem
                    {
                        Text = Language.Strings.Remove,
                        IconSource = new FluentIconSource { Icon = Icon.Delete }
                    };
                    removeItem.Click += (_, _) =>
                    {
                        viewModel.Presets.Items.Remove(preset);
                        viewModel.Presets.SaveItems();
                    };
                    menu.Items.Add(removeItem);

                    var renameItem = new FAMenuFlyoutItem { Text = Language.Strings.Rename };
                    renameItem.Click += (_, _) => ShowRenameFlyout(preset, args.Anchor);
                    menu.Items.Add(renameItem);
                    break;
                }
        }

        menu.ShowAt(args.Anchor);
    }

    private void ShowRenameFlyout(object target, Control anchor)
    {
        if (DataContext is not OutputTabViewModel viewModel) return;

        var profile = target as OutputProfileItem;
        var preset = target as OutputPresetItem;
        var flyout = new RenameFlyout { Text = profile?.Context.Name.Value ?? preset?.Name.Value };
        flyout.Confirmed += (_, text) =>
        {
            if (profile != null)
            {
                profile.Context.Name.Value = text ?? "";
                viewModel.Save();
            }
            else if (preset != null)
            {
                preset.Name.Value = text ?? "";
                viewModel.Presets.SaveItems();
            }
        };
        flyout.ShowAt(anchor);
    }
}
