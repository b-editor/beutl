using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Data;
using BEditor.Models.ManagePlugins;
using BEditor.Packaging;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;

namespace BEditor.ViewModels.Dialogs
{
    public sealed class InstallRequiredPluginsViewModel
    {
        public class Item
        {
            public Item(bool isFound, ProjectPackage.PluginInfo info, bool isSelected)
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

        public InstallRequiredPluginsViewModel(ProjectPackage.PluginInfo[] requiredPlugin)
        {
            InstallLater = new(CanInstall);
            InstallNow = new(CanInstall);
            Task.Run(async () =>
            {
                var client = ServicesLocator.Current.Provider.GetRequiredService<HttpClient>();
                var queue = new Queue<Item>(requiredPlugin.Select(i => new Item(false, i, false)));
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
                            Items.Add(pluginInfo);
                            break;
                        }
                    }

                    if (!pluginInfo.IsFound)
                        Items.Add(pluginInfo);
                }

                IsLoaded.Value = false;
                CanInstall.Value = true;
            });

            InstallLater.Subscribe(() => Register());

            InstallNow.Subscribe(() => Register());
        }

        public ObservableCollection<Item> Items { get; } = new();

        public ReactivePropertySlim<bool> IsLoaded { get; } = new(true);

        public ReactivePropertySlim<bool> CanInstall { get; } = new(false);

        public ReactiveCommand InstallLater { get; }

        public ReactiveCommand InstallNow { get; }

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
            foreach (var item in Items)
            {
                if (item.IsSelected && item.IsFound)
                {
                    PluginChangeSchedule.UpdateOrInstall.Add(new(item.Package!, item.Version!, PluginChangeType.Install));
                }
            }
        }
    }
}
