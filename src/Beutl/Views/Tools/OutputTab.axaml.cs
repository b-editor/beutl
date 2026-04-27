using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Beutl.Controls;
using Beutl.Controls.PropertyEditors;
using Beutl.Services;
using Beutl.ViewModels.Dialogs;
using Beutl.ViewModels.Tools;
using FluentAvalonia.UI.Controls;
using AddOutputProfileDialog = Beutl.Views.Dialogs.AddOutputProfileDialog;

namespace Beutl.Views.Tools;

public partial class OutputTab : UserControl
{
    private readonly IDataTemplate _sharedDataTemplate = new _DataTemplate();
    private OutputPickerFlyout? _activeFlyout;

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
        if (sender is ICommandSource { CommandParameter: OutputProfileItem item })
        {
            viewModel.RemoveItem(item);
            viewModel.Save();
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
        if ((sender as ICommandSource)?.CommandParameter is not OutputProfileItem item) return;

        var flyout = new RenameFlyout { Text = item.Context.Name.Value };
        flyout.Confirmed += (_, text) =>
        {
            item.Context.Name.Value = text ?? "";
            viewModel.Save();
        };
        flyout.ShowAt(MoreButton);
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
        if (DataContext is not OutputTabViewModel viewModel) return;

        var pickerVm = new OutputPickerViewModel(viewModel.Items, viewModel.PresetItems);
        pickerVm.SetInitialSelection(viewModel.SelectedItem.Value);

        var flyout = new OutputPickerFlyout(pickerVm);
        _activeFlyout = flyout;
        flyout.Confirmed += OnPickerConfirmed;
        flyout.Dismissed += OnPickerDismissed;
        flyout.MoreMenuRequested += OnPickerMoreMenuRequested;
        flyout.Closed += (_, _) =>
        {
            flyout.Confirmed -= OnPickerConfirmed;
            flyout.Dismissed -= OnPickerDismissed;
            flyout.MoreMenuRequested -= OnPickerMoreMenuRequested;
            pickerVm.Dispose();
            if (ReferenceEquals(_activeFlyout, flyout)) _activeFlyout = null;
        };

        flyout.ShowAt(ProfilesButton, true);
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

    private void OnPickerDismissed(OutputPickerFlyout sender, EventArgs e)
    {
    }

    private void OnPickerMoreMenuRequested(OutputPickerFlyout sender, MoreMenuRequestedArgs args)
    {
        if (DataContext is not OutputTabViewModel viewModel) return;

        var menu = new FAMenuFlyout();

        switch (args.Item.UserData)
        {
            case OutputProfileItem profile:
                {
                    var removeItem = new MenuFlyoutItem
                    {
                        Text = Language.Strings.Remove,
                        IconSource = new SymbolIconSource { Symbol = Symbol.Delete }
                    };
                    removeItem.Click += (_, _) =>
                    {
                        viewModel.RemoveItem(profile);
                        viewModel.Save();
                    };
                    menu.Items.Add(removeItem);

                    var renameItem = new MenuFlyoutItem { Text = Language.Strings.Rename };
                    renameItem.Click += (_, _) => ShowRenameFlyout(profile, args.Anchor);
                    menu.Items.Add(renameItem);

                    var convertItem = new MenuFlyoutItem { Text = Language.Strings.Convert_to_preset };
                    convertItem.Click += (_, _) =>
                    {
                        OutputPresetService.Instance.AddItem(profile.Context, $"{profile.Context.Name.Value} (Preset)");
                        OutputPresetService.Instance.SaveItems();
                    };
                    menu.Items.Add(convertItem);
                    break;
                }
            case OutputPresetItem preset:
                {
                    var removeItem = new MenuFlyoutItem
                    {
                        Text = Language.Strings.Remove,
                        IconSource = new SymbolIconSource { Symbol = Symbol.Delete }
                    };
                    removeItem.Click += (_, _) =>
                    {
                        OutputPresetService.Instance.Items.Remove(preset);
                        OutputPresetService.Instance.SaveItems();
                    };
                    menu.Items.Add(removeItem);

                    var renameItem = new MenuFlyoutItem { Text = Language.Strings.Rename };
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
                OutputPresetService.Instance.SaveItems();
            }
        };
        flyout.ShowAt(anchor);
    }
}
