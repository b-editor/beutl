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
using BEditor.LangResources;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;

namespace BEditor.ViewModels.ManagePlugins
{
    public sealed class UpdateViewModel
    {
        public UpdateViewModel()
        {
            UpdateService = ServicesLocator.Current.Provider.GetRequiredService<PluginUpdateService>();

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

            Refresh.Subscribe(() =>
            {
                IsLoading.Value = true;
                UpdateService.CheckUpdateAsync().ContinueWith(_ => IsLoading.Value = false);
            });

            UpdateService.CheckUpdateAsync().ContinueWith(_ => IsLoading.Value = false);
        }

        public ReadOnlyReactivePropertySlim<bool> IsSelected { get; }

        public ReactivePropertySlim<bool> IsLoading { get; } = new(true);

        public ReactivePropertySlim<PluginUpdateService.UpdateTarget> SelectedItem { get; } = new();

        public ReactiveProperty<PackageVersion> SelectedVersion { get; } = new();

        public ReactiveCommand Update { get; } = new();

        public ReactiveCommand Cancel { get; } = new();

        public ReactiveCommand Refresh { get; } = new();

        public ReadOnlyReactivePropertySlim<bool> IsScheduled { get; }

        public PluginUpdateService UpdateService { get; }
    }
}