using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Models;
using BEditor.Models.ManagePlugins;
using BEditor.Packaging;
using BEditor.Plugin;
using BEditor.Properties;

using Reactive.Bindings;

namespace BEditor.ViewModels.ManagePlugins
{
    public class UpdateViewModel
    {
        public record UpdateTarget(PluginObject Plugin, Package Package)
        {
            public string OldVersion => GetVersion(Plugin)!.ToString(3);

            public string NewVersion => Package.Versions.First().Version;

            public PackageVersion NewerVersion => Package.Versions.First();
        }

        public UpdateViewModel()
        {
            IsSelected = SelectedItem.Select(i => i is not null)
                .ToReadOnlyReactivePropertySlim();

            IsScheduled = SelectedItem.Select(p => PluginChangeSchedule.UpdateOrInstall.Any(i => i.Target == p.Package))
                .ToReadOnlyReactivePropertySlim();

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
                        PluginChangeSchedule.UpdateOrInstall.Add(new(SelectedItem.Value.Package, PluginChangeType.Update));
                    }
                    else
                    {
                        await AppModel.Current.Message.DialogAsync(Strings.ThisNameAlreadyExists);
                    }

                    SelectedItem.ForceNotify();
                });
        }

        [AllowNull]
        public LibraryViewModel Library { get; private set; }

        public ReadOnlyReactivePropertySlim<bool> IsSelected { get; }

        public ReactivePropertySlim<UpdateTarget> SelectedItem { get; } = new();

        public ReactiveCollection<UpdateTarget> Items { get; } = new();

        public ReactiveCommand Update { get; } = new();

        public ReactiveCommand Cancel { get; } = new();

        public ReadOnlyReactivePropertySlim<bool> IsScheduled { get; }

        public void Initialize(LibraryViewModel library)
        {
            library.LoadTask.ContinueWith(_ =>
            {
                Items.AddRangeOnScheduler(library.PackageSources
                    .SelectMany(i => i.Packages)
                    .Select(i => (plugin: PluginManager.Default.Plugins.FirstOrDefault(p => p.Id == i.Id), package: i))
                    .Where(i => i.plugin is not null &&
                        i.package.Versions.FirstOrDefault() is PackageVersion packageVersion &&
                        GetVersion(i.plugin!) < new Version(packageVersion.Version))
                     .Select(i => new UpdateTarget(i.plugin!, i.package)));
            });
        }

        private static Version? GetVersion(PluginObject plugin)
        {
            return plugin.GetType().Assembly.GetName().Version;
        }
    }
}