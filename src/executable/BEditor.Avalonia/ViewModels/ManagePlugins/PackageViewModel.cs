using System;
using System.Linq;
using System.Reactive.Linq;

using Avalonia.Dialogs;

using BEditor.Models.ManagePlugins;
using BEditor.Packaging;
using BEditor.Plugin;

using Reactive.Bindings;

namespace BEditor.ViewModels.ManagePlugins
{
    public sealed class PackageViewModel
    {
        public PackageViewModel(Package package)
        {
            Package.Value = package;
            CanCancel = Package.Select(_ => PluginChangeSchedule.UpdateOrInstall.Any(i => i.Target == Package.Value)
                || PluginChangeSchedule.Uninstall.Any(i => i.Id == Package.Value.Id))
                .ToReadOnlyReactivePropertySlim();

            CanInstall = Package.Select(_ => !PluginManager.Default.Plugins.Any(i => i.Id == Package.Value.Id) && !CanCancel.Value)
                .ToReadOnlyReactivePropertySlim();

            CanUpdate = Package.Select(_ =>
            {
                var plugin = PluginManager.Default.Plugins.FirstOrDefault(p => p.Id == package.Id);

                return plugin != null
                    && package.Versions.FirstOrDefault() is PackageVersion packageVersion
                    && GetVersion(plugin) < new Version(packageVersion.Version)
                    && !CanCancel.Value;
            }).ToReadOnlyReactivePropertySlim();

            CanUninstall = Package.Select(_ => PluginManager.Default.Plugins.Any(i => i.Id == Package.Value.Id) && !CanCancel.Value)
                .ToReadOnlyReactivePropertySlim();

            CanSelectVersion = Package.Select(_ => CanInstall.Value || CanUpdate.Value).ToReadOnlyReactivePropertySlim();

            OpenHomePage.Subscribe(_ => AboutAvaloniaDialog.OpenBrowser(Package.Value.HomePage));

            SelectedVersion.Value = Package.Value.Versions[0];

            Install.Where(_ => CanInstall.Value)
                .Subscribe(_ =>
                {
                    PluginChangeSchedule.UpdateOrInstall.Add(new(Package.Value, SelectedVersion.Value!, PluginChangeType.Install));
                    Package.ForceNotify();
                });

            Update.Where(_ => CanUpdate.Value)
                .Subscribe(_ =>
                {
                    PluginChangeSchedule.UpdateOrInstall.Add(new(Package.Value, SelectedVersion.Value!, PluginChangeType.Install));
                    Package.ForceNotify();
                });

            Uninstall.Where(_ => CanUninstall.Value)
                .Subscribe(_ =>
                {
                    var plugin = PluginManager.Default.Plugins.FirstOrDefault(i => i.Id == Package.Value.Id);
                    if (plugin == null) return;

                    PluginChangeSchedule.Uninstall.Add(plugin);
                    Package.ForceNotify();
                });

            CancelChange.Where(_ => CanCancel.Value)
                .Subscribe(_ =>
                {
                    var item = PluginChangeSchedule.UpdateOrInstall.FirstOrDefault(i => i.Target == Package.Value);
                    var plugin = PluginChangeSchedule.Uninstall.FirstOrDefault(i => i.Id == Package.Value.Id);
                    if (item != null)
                    {
                        PluginChangeSchedule.UpdateOrInstall.Remove(item);
                    }

                    if (plugin != null)
                    {
                        PluginChangeSchedule.Uninstall.Remove(plugin);
                    }

                    Package.ForceNotify();
                });
        }

        public ReadOnlyReactivePropertySlim<bool> CanCancel { get; }

        public ReadOnlyReactivePropertySlim<bool> CanInstall { get; }

        public ReadOnlyReactivePropertySlim<bool> CanUpdate { get; }

        public ReadOnlyReactivePropertySlim<bool> CanUninstall { get; }

        public ReadOnlyReactivePropertySlim<bool> CanSelectVersion { get; }

        public ReactiveCommand OpenHomePage { get; } = new();

        public ReactiveProperty<PackageVersion?> SelectedVersion { get; } = new();

        public ReactiveCommand Install { get; } = new();

        public ReactiveCommand Update { get; } = new();

        public ReactiveCommand Uninstall { get; } = new();

        public ReactiveCommand CancelChange { get; } = new();

        public ReactivePropertySlim<Package> Package { get; } = new();

        private static Version? GetVersion(PluginObject plugin)
        {
            return plugin.GetType().Assembly.GetName().Version;
        }
    }
}