using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net.Http;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Models;
using BEditor.Models.ManagePlugins;
using BEditor.Packaging;
using BEditor.Plugin;
using BEditor.Properties;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;

namespace BEditor.ViewModels.ManagePlugins
{
    public sealed class UpdateViewModel
    {
        private readonly HttpClient _client;

        public sealed record UpdateTarget(PluginObject Plugin, Package Package)
        {
            public string OldVersion => GetVersion(Plugin)!.ToString(3);

            public string NewVersion => Package.Versions.First().Version;

            public PackageVersion NewerVersion => Package.Versions.First();
        }

        public UpdateViewModel()
        {
            _client = ServicesLocator.Current.Provider.GetRequiredService<HttpClient>();

            IsSelected = SelectedItem.Select(i => i is not null)
                .ToReadOnlyReactivePropertySlim();

            IsScheduled = SelectedItem.Where(i => i is not null).Select(p => PluginChangeSchedule.UpdateOrInstall.Any(i => i.Target == p.Package))
                .ToReadOnlyReactivePropertySlim();

            SelectedVersion = SelectedItem.Select(i => i?.Package?.Versions?[0]).ToReactiveProperty()!;

            Cancel.Subscribe(async () =>
            {
                if (IsScheduled.Value &&
                    PluginChangeSchedule.UpdateOrInstall.FirstOrDefault(i => i.Target == SelectedItem.Value.Package) is var item &&
                    item is not null)
                {
                    PluginChangeSchedule.UpdateOrInstall.Remove(item);
                }
                else
                {
                    await AppModel.Current.Message.DialogAsync(Strings.AlreadyCancelled);
                }

                SelectedItem.ForceNotify();
            });

            Update.Where(_ => IsSelected.Value)
                .Subscribe(async _ =>
                {
                    if (!IsScheduled.Value)
                    {
                        PluginChangeSchedule.UpdateOrInstall.Add(new(SelectedItem.Value.Package, SelectedVersion.Value, PluginChangeType.Update));
                    }
                    else
                    {
                        await AppModel.Current.Message.DialogAsync(Strings.ThisNameAlreadyExists);
                    }

                    SelectedItem.ForceNotify();
                });

            Load();
        }

        public ReadOnlyReactivePropertySlim<bool> IsSelected { get; }

        public ReactivePropertySlim<bool> IsLoading { get; } = new(true);

        public ReactivePropertySlim<UpdateTarget> SelectedItem { get; } = new();

        public ReactiveCollection<UpdateTarget> Items { get; } = new();

        public ReactiveProperty<PackageVersion> SelectedVersion { get; } = new();

        public ReactiveCommand Update { get; } = new();

        public ReactiveCommand Cancel { get; } = new();

        public ReadOnlyReactivePropertySlim<bool> IsScheduled { get; }

        private async void Load()
        {
            foreach (var item in BEditor.Settings.Default.PackageSources)
            {
                var repos = await item.ToRepositoryAsync(_client);
                if (repos is null) continue;

                foreach (var package in repos.Packages)
                {
                    var plugin = PluginManager.Default.Plugins.FirstOrDefault(p => p.Id == package.Id);

                    if (plugin is not null
                        && package.Versions.FirstOrDefault() is PackageVersion packageVersion
                        && GetVersion(plugin) < new Version(packageVersion.Version))
                    {
                        Items.AddOnScheduler(new UpdateTarget(plugin, package));
                    }
                }
            }
            IsLoading.Value = false;
        }

        private static Version? GetVersion(PluginObject plugin)
        {
            return plugin.GetType().Assembly.GetName().Version;
        }
    }
}