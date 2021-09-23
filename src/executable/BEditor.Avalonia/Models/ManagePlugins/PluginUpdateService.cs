using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

using BEditor.Packaging;
using BEditor.Plugin;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;

namespace BEditor.Models.ManagePlugins
{
    public sealed class PluginUpdateService
    {
        public sealed record UpdateTarget(PluginObject Plugin, Package Package)
        {
            public string OldVersion => GetVersion(Plugin)!.ToString(3);

            public string NewVersion => Package.Versions.First().Version;

            public PackageVersion NewerVersion => Package.Versions.First();
        }

        public ReactiveCollection<UpdateTarget> Updates { get; } = new();

        public async Task CheckUpdateAsync()
        {
            var client = ServicesLocator.Current.Provider.GetRequiredService<HttpClient>();
            Updates.ClearOnScheduler();

            foreach (var item in Settings.Default.PackageSources)
            {
                var repos = await item.ToRepositoryAsync(client);
                if (repos is null) continue;

                foreach (var package in repos.Packages)
                {
                    var plugin = PluginManager.Default.Plugins.FirstOrDefault(p => p.Id == package.Id);

                    if (plugin is not null
                        && package.Versions.FirstOrDefault() is PackageVersion packageVersion
                        && GetVersion(plugin) < new Version(packageVersion.Version))
                    {
                        Updates.AddOnScheduler(new UpdateTarget(plugin, package));
                    }
                }
            }
        }

        private static Version? GetVersion(PluginObject plugin)
        {
            return plugin.GetType().Assembly.GetName().Version;
        }
    }
}