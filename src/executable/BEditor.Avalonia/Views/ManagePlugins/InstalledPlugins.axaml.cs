using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

using BEditor.Properties;
using BEditor.ViewModels.ManagePlugins;

namespace BEditor.Views.ManagePlugins
{
    public sealed class LoadedPlugins : UserControl
    {
        private readonly ListBox _pluginList;
        private readonly ContentControl _pluginSettings;
        private readonly Button _settingsButton;
        private readonly Button _applySettingsButton;

        public LoadedPlugins()
        {
            InitializeComponent();
            _pluginList = this.FindControl<ListBox>("PluginList");
            _pluginSettings = this.FindControl<ContentControl>("PluginSettings");
            _settingsButton = this.FindControl<Button>("SettingsButton");
            _applySettingsButton = this.FindControl<Button>("ApplySettingsButton");
        }

        public void ShowSettings(object s, RoutedEventArgs e)
        {
            if (_pluginSettings.IsVisible)
            {
                _pluginSettings.Content = null;
                _pluginSettings.IsVisible = false;
                _applySettingsButton.IsVisible = false;

                _pluginList.IsVisible = true;
                _settingsButton.Content = Strings.Settings;
            }
            else if (DataContext is LoadedPluginsViewModel vm && vm.IsSelected.Value)
            {
                _pluginSettings.Content = (StackPanel?)PluginSettingsUIBuilder.Create(vm.SelectPlugin.Value.Settings);
                _pluginSettings.IsVisible = true;
                _applySettingsButton.IsVisible = true;

                _pluginList.IsVisible = false;
                _settingsButton.Content = Strings.Back;
            }
        }

        public void ApplySettings(object s, RoutedEventArgs e)
        {
            if (DataContext is LoadedPluginsViewModel vm && vm.IsSelected.Value)
            {
                var ctr = (StackPanel)_pluginSettings.Content!;

                vm.SelectPlugin.Value.Settings = (Plugin.SettingRecord)PluginSettingsUIBuilder.GetValue(ctr, vm.SelectPlugin.Value.Settings.GetType());
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}