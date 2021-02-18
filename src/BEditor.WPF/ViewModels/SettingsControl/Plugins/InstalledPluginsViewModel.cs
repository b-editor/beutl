using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.DirectoryServices.ActiveDirectory;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;

using BEditor.Data;
using BEditor.Models;
using BEditor.Plugin;
using BEditor.Views;
using BEditor.Views.SettingsControl;

using Reactive.Bindings;

namespace BEditor.ViewModels.SettingsControl.Plugins
{
    public class InstalledPluginsViewModel : BasePropertyChanged
    {
        public InstalledPluginsViewModel()
        {
            SettingClick.Where(_ => SelectPlugin is not null).Subscribe(_ =>
            {
                var type = SelectPlugin.Value.Settings.GetType();
                var ui = UIBuilderFromRecord.Create(SelectPlugin.Value.Settings);
                var dialog = new NoneDialog(ui);

                dialog.ShowDialog();

                SelectPlugin.Value.Settings = (SettingRecord)UIBuilderFromRecord.GetValue(ui, type);
            });
            UnloadClick.Where(_ => SelectPlugin is not null)
                .Subscribe(_ =>
            {
                var name = SelectPlugin.Value.AssemblyName;

                Settings.Default.EnablePlugins.Remove(name);
                if (!Settings.Default.DisablePlugins.Contains(name))
                    Settings.Default.DisablePlugins.Add(name);

                Settings.Default.Save();
            });

            IsSelected = SelectPlugin.Select(plugin => plugin is not null).ToReadOnlyReactiveProperty();
        }

        public ReactiveProperty<IPlugin> SelectPlugin { get; } = new();

        public ReadOnlyReactiveProperty<bool> IsSelected { get; }
        public ReactiveCommand<object> SettingClick { get; } = new();
        public ReactiveCommand UnloadClick { get; } = new();
    }
}
