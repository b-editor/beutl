using System;
using System.Linq;
using System.Reactive.Linq;

using Avalonia.Controls.ApplicationLifetimes;

using BEditor.Plugin;

using MessageBox.Avalonia.DTO;

using Reactive.Bindings;

namespace BEditor.ViewModels.Settings
{
    public class InstalledPluginsViewModel
    {
        public InstalledPluginsViewModel()
        {
            SettingClick.Where(_ => SelectPlugin is not null).Subscribe(_ =>
            {
                if (Avalonia.Application.Current.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    MessageBox.Avalonia.MessageBoxManager.GetMessageBoxStandardWindow(new MessageBoxStandardParams
                    {
                        ContentMessage = "未実装"
                    }).ShowDialog(desktop.MainWindow);
                }
            });
            UnloadClick.Where(_ => SelectPlugin is not null)
                .Subscribe(_ =>
                {
                    var name = SelectPlugin.Value.AssemblyName;

                    BEditor.Settings.Default.EnablePlugins.Remove(name);
                    if (!BEditor.Settings.Default.DisablePlugins.Contains(name))
                        BEditor.Settings.Default.DisablePlugins.Add(name);

                    BEditor.Settings.Default.Save();
                });

            IsSelected = SelectPlugin.Select(plugin => plugin is not null).ToReadOnlyReactiveProperty();
        }

        public ReactiveProperty<PluginObject> SelectPlugin { get; } = new();

        public ReadOnlyReactiveProperty<bool> IsSelected { get; }
        public ReactiveCommand<object> SettingClick { get; } = new();
        public ReactiveCommand UnloadClick { get; } = new();
    }
}
