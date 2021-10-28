using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

using BEditor.Plugin;
using BEditor.ViewModels.ManagePlugins;

using FluentAvalonia.UI.Controls;

namespace BEditor.Views.ManagePlugins
{
    public sealed class LoadedPlugins : UserControl
    {
        private readonly ListBox _pluginList;
        private readonly ContentControl _pluginSettings;
        private readonly CommandBarButton _backButton;
        private readonly CommandBarButton _applySettingsButton;

        public LoadedPlugins()
        {
            InitializeComponent();
            _pluginList = this.FindControl<ListBox>("PluginList");
            _pluginSettings = this.FindControl<ContentControl>("PluginSettings");
            _backButton = this.FindControl<CommandBarButton>("BackButton");
            _applySettingsButton = this.FindControl<CommandBarButton>("ApplySettingsButton");
        }

        public void ShowSettings(object s, RoutedEventArgs e)
        {
            if (s is CommandBarButton button
                && button.DataContext is PluginObject plugin
                && DataContext is LoadedPluginsViewModel vm)
            {
                vm.SelectPlugin.Value = plugin;
                _pluginSettings.Content = (StackPanel?)PluginSettingsUIBuilder.Create(plugin.Settings);
                _pluginSettings.IsVisible = true;
                _applySettingsButton.IsVisible = true;

                _pluginList.IsVisible = false;
                _backButton.IsVisible = true;
            }
        }

        public void BackClick(object s, RoutedEventArgs e)
        {
            if (_pluginSettings.IsVisible)
            {
                _pluginSettings.Content = null;
                _pluginSettings.IsVisible = false;
                _applySettingsButton.IsVisible = false;

                _pluginList.IsVisible = true;
                _backButton.IsVisible = false;
            }
        }

        public void ApplySettings(object s, RoutedEventArgs e)
        {
            if (DataContext is LoadedPluginsViewModel vm && vm.IsSelected.Value)
            {
                var ctr = (StackPanel)_pluginSettings.Content!;

                vm.SelectPlugin.Value.Settings = (SettingRecord)PluginSettingsUIBuilder.GetValue(ctr, vm.SelectPlugin.Value.Settings.GetType());
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}