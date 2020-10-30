using System;
using System.Collections.Generic;
using System.DirectoryServices.ActiveDirectory;
using System.IO;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;

using BEditor.Models;
using BEditor.ViewModels.Helper;
using BEditor.NET.Extesions.ViewCommand;
using BEditor.NET.Plugin;

namespace BEditor.ViewModels.SettingsControl.Plugins {
    public class InstalledPluginsViewModel : BasePropertyChanged {

        private IPlugin selectplugin;
        public InstalledPluginsViewModel() {

            SettingClick.Subscribe(_ => {
                Message.Snackbar(SelectPlugin?.PluginName);

                SelectPlugin.SettingCommand();
            });
        }

        public IPlugin SelectPlugin { get => selectplugin; set => SetValue(value, ref selectplugin, nameof(SelectPlugin)); }
        public DelegateCommand<object> SettingClick { get; } = new DelegateCommand<object>();
    }
}
