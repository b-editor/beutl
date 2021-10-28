using System;
using System.Linq;
using System.Reactive.Linq;

using BEditor.LangResources;
using BEditor.Models.ManagePlugins;
using BEditor.Plugin;

using Reactive.Bindings;

namespace BEditor.ViewModels.ManagePlugins
{
    public sealed class LoadedPluginsViewModel
    {
        public LoadedPluginsViewModel()
        {
            Plugins = new();
            Plugins.AddRangeOnScheduler(PluginManager.Default.Plugins);

            Plugins.AddRangeOnScheduler(PluginManager.Default.Failed.Select(
                asm => new DummyPlugin(PluginBuilder.Config!, asm.GetName().Name ?? string.Empty, string.Format(Strings.FailedToLoad, Strings.Plugin), Guid.Empty, asm)));

            IsSelected = SelectPlugin.Select(plugin => plugin is not null).ToReadOnlyReactivePropertySlim();

            Uninstall.Where(_ => IsSelected.Value)
                .Subscribe(_ =>
                {
                    PluginChangeSchedule.Uninstall.Add(SelectPlugin.Value);
                    SelectPlugin.ForceNotify();
                });

            Cancel.Where(_ => IsSelected.Value)
                .Subscribe(_ =>
                {
                    PluginChangeSchedule.Uninstall.Remove(SelectPlugin.Value);
                    SelectPlugin.ForceNotify();
                });

            UninstallVisible = SelectPlugin
                .Select(i => !PluginChangeSchedule.Uninstall.Contains(i) && IsSelected.Value)
                .ToReadOnlyReactivePropertySlim();

            CancelVisible = SelectPlugin
                .Select(i => PluginChangeSchedule.Uninstall.Contains(i) && IsSelected.Value)
                .ToReadOnlyReactivePropertySlim();
        }

        public ReactiveCollection<PluginObject> Plugins { get; }

        public ReactiveProperty<PluginObject> SelectPlugin { get; } = new();

        public ReadOnlyReactivePropertySlim<bool> IsSelected { get; }

        public ReadOnlyReactivePropertySlim<bool> UninstallVisible { get; }

        public ReadOnlyReactivePropertySlim<bool> CancelVisible { get; }

        public ReactiveCommand Uninstall { get; } = new();

        public ReactiveCommand Cancel { get; } = new();
    }
}