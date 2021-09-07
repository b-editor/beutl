using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

using BEditor.Data;
using BEditor.Models;
using BEditor.Models.ManagePlugins;
using BEditor.Packaging;
using BEditor.Plugin;
using BEditor.Properties;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;

namespace BEditor.ViewModels.Dialogs
{
    public sealed class OpenProjectPackageViewModel
    {
        public enum State
        {
            InstallLater,
            InstallNow,
            Close,
            Open,
        }

        public sealed class PluginItem
        {
            public PluginItem(bool isFound, ProjectPackage.PluginInfo info, bool isSelected)
            {
                IsFound = isFound;
                Info = info;
                IsSelected = isSelected;
            }

            public bool IsFound { get; set; }

            public bool IsNotFound => !IsFound;

            public ProjectPackage.PluginInfo Info { get; set; }

            public Package? Package { get; set; }

            public PackageVersion? Version { get; set; }

            public bool IsSelected { get; set; }
        }

        public OpenProjectPackageViewModel(string file)
        {
            InstallLater = new(CanInstall);
            InstallNow = new(CanInstall);
            ReadMe.Value = ProjectPackage.GetReadMe(file);
            var plugins = ProjectPackage.GetPluginInfo(file);
            var installed = PluginManager.Default.Plugins.Select(i => new ProjectPackage.PluginInfo(i));
            var requiredPlugin = plugins.Except(installed).ToArray();

            Task.Run(async () =>
            {
                var client = ServicesLocator.Current.Provider.GetRequiredService<HttpClient>();
                var queue = new Queue<PluginItem>(requiredPlugin.Select(i => new PluginItem(false, i, false)));
                var pkgs = (await LoadAsync()).SelectMany(i => i.Packages);

                while (queue.TryDequeue(out var pluginInfo))
                {
                    foreach (var pkg in pkgs)
                    {
                        if (pkg.Id == pluginInfo.Info.Id)
                        {
                            var v = new Version(pluginInfo.Info.Version);
                            pluginInfo.Version = Array.Find(pkg.Versions, i => new Version(i.Version) == v) ?? pkg.Versions.Max();
                            pluginInfo.IsFound = true;
                            pluginInfo.Package = pkg;
                            Plugins.Add(pluginInfo);
                            break;
                        }
                    }

                    if (!pluginInfo.IsFound)
                        Plugins.Add(pluginInfo);
                }

                IsLoaded.Value = false;
                CanInstall.Value = true;
                if (Plugins.Any()) TabIndex.Value = 0;
                else TabIndex.Value = 1;
            });

            InstallLater.Subscribe(() =>
            {
                Register();
                Close.Execute(State.InstallLater);
            });

            InstallNow.Subscribe(() =>
            {
                Register();
                App.Shutdown(0);
            });

            Open.Subscribe(async () =>
            {
                if (Plugins.Any())
                {
                    var msg = AppModel.Current.Message;
                    var result = await msg.DialogAsync(
                         Strings.YouDoNotHaveTheRequiredPluginsInstalled,
                         IMessage.IconType.Info,
                         new IMessage.ButtonType[] { IMessage.ButtonType.Yes, IMessage.ButtonType.No });

                    if (result == IMessage.ButtonType.Yes)
                    {
                        InstallNow.Execute();
                    }
                    else
                    {
                        Close.Execute(State.Close);
                    }
                }
                else
                {
                    Close.Execute(State.Open);
                }
            });
        }

        public ReactivePropertySlim<int> TabIndex { get; } = new(1);

        public ObservableCollection<PluginItem> Plugins { get; } = new();

        public ReactivePropertySlim<bool> CanInstall { get; } = new(false);

        public ReactiveCommand InstallLater { get; }

        public ReactiveCommand InstallNow { get; }

        public ReactivePropertySlim<string> ReadMe { get; } = new();

        public ReactiveCommand<State> Close { get; } = new();

        public ReactiveCommand Open { get; } = new();

        public ReactivePropertySlim<bool> IsLoaded { get; } = new(true);

        private static async Task<List<PackageSource>> LoadAsync()
        {
            var client = ServicesLocator.Current.Provider.GetRequiredService<HttpClient>();
            var list = new List<PackageSource>();

            for (var i = 0; i < BEditor.Settings.Default.PackageSources.Count; i++)
            {
                var item = BEditor.Settings.Default.PackageSources[i];
                var src = await item.ToRepositoryAsync(client);
                if (src != null)
                {
                    list.Add(src);
                }
            }

            return list;
        }

        private void Register()
        {
            foreach (var item in Plugins)
            {
                if (item.IsSelected && item.IsFound)
                {
                    PluginChangeSchedule.UpdateOrInstall.Add(new(item.Package!, item.Version!, PluginChangeType.Install));
                }
            }
        }
    }
}
