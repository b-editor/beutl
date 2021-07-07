using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Reactive.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using Avalonia.Dialogs;

using BEditor.Models;
using BEditor.Models.ManagePlugins;
using BEditor.Packaging;
using BEditor.Plugin;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;

using Setting = BEditor.Settings;

namespace BEditor.ViewModels.ManagePlugins
{
    public class LibraryViewModel
    {
        private readonly HttpClient _client;
        private Package[]? _loadedItems;

        public LibraryViewModel()
        {
            _client = AppModel.Current.ServiceProvider.GetRequiredService<HttpClient>();
            SelectedSource.Where(i => i is not null)
                .Subscribe(src =>
                {
                    _loadedItems = src!.Packages;
                    Page.Value = 1;
                    Page.ForceNotify();
                    ReloadItems();
                });

            ExistNextPage = Page.Where(_ => _loadedItems is not null)
                .Select(i => _loadedItems!.Skip(i * 20).Take(20).Any())
                .ToReadOnlyReactivePropertySlim();

            ExistPrevPage = Page.Where(_ => SelectedSource.Value is not null)
                .Select(i => i is not 1)
                .ToReadOnlyReactivePropertySlim();

            NextPage.Where(_ => ExistNextPage.Value && SelectedSource.Value is not null)
                .Subscribe(_ =>
                {
                    Page.Value++;
                    ReloadItems();
                });

            PrevPage.Where(_ => ExistPrevPage.Value && SelectedSource.Value is not null)
                .Subscribe(_ =>
                {
                    Page.Value--;
                    ReloadItems();
                });

            Search.Where(_ => SelectedSource.Value is not null).Subscribe(_ =>
            {
                var str = SearchText.Value;
                if (string.IsNullOrWhiteSpace(str))
                {
                    _loadedItems = SelectedSource.Value!.Packages;

                    ReloadItems();
                    Page.Value = 1;
                    Page.ForceNotify();

                    return;
                }

                var regices = SearchService.CreateRegices(str);

                _loadedItems = SelectedSource.Value!.Packages
                    .Where(i => SearchService.IsMatch(regices, i.Name)
                    || SearchService.IsMatch(regices, i.Description)
                    || SearchService.IsMatch(regices, i.Tag))
                    .ToArray();

                ReloadItems();

                Page.Value = 1;
                Page.ForceNotify();
            });

            IsSelected = SelectedItem.Select(i => i is not null).ToReadOnlyReactivePropertySlim();

            CancelIsVisible = SelectedItem.Where(i => i is not null)
                .Select(_ => PluginChangeSchedule.UpdateOrInstall.Any(i => i.Target == SelectedItem.Value))
                .ToReadOnlyReactivePropertySlim();

            InstallIsVisible = SelectedItem.Where(i => i is not null)
                .Select(_ => !PluginManager.Default.Plugins.Any(i => i.Id == SelectedItem.Value!.Id) && !CancelIsVisible.Value)
                .ToReadOnlyReactivePropertySlim();

            OpenHomePage.Where(_ => IsSelected.Value)
                .Subscribe(_ => AboutAvaloniaDialog.OpenBrowser(SelectedItem.Value!.HomePage));

            LoadTask = Task.Run(async () =>
            {
                foreach (var item in Setting.Default.PackageSources)
                {
                    var repos = await item.ToRepositoryAsync(_client);
                    if (repos is null) continue;
                    PackageSources.Add(repos);
                }

                SelectedSource.Value = PackageSources.FirstOrDefault();
                IsLoaded.Value = false;
            });

            SelectedVersion = SelectedItem.Select(i => i?.Versions?[0]).ToReactiveProperty();

            Install.Where(_ => InstallIsVisible.Value)
                .Subscribe(_ =>
                {
                    PluginChangeSchedule.UpdateOrInstall.Add(new(SelectedItem.Value!, SelectedVersion.Value!, PluginChangeType.Install));
                    SelectedItem.ForceNotify();
                });

            CancelChange.Where(_ => CancelIsVisible.Value)
                .Subscribe(_ =>
                {
                    var item = PluginChangeSchedule.UpdateOrInstall.FirstOrDefault(i => i.Target == SelectedItem.Value);
                    if (item is not null)
                    {
                        PluginChangeSchedule.UpdateOrInstall.Remove(item);
                        SelectedItem.ForceNotify();
                    }
                });
        }

        public ReactiveCommand NextPage { get; } = new();

        public ReactiveCommand PrevPage { get; } = new();

        public ReadOnlyReactivePropertySlim<bool> ExistNextPage { get; }

        public ReadOnlyReactivePropertySlim<bool> ExistPrevPage { get; }

        public ReactivePropertySlim<int> Page { get; } = new(0);

        public ReactiveCommand Search { get; } = new();

        public ReactivePropertySlim<string> SearchText { get; } = new(string.Empty);

        public ReactivePropertySlim<bool> IsLoaded { get; } = new(true);

        public ReadOnlyReactivePropertySlim<bool> IsSelected { get; }

        public ReadOnlyReactivePropertySlim<bool> CancelIsVisible { get; }

        public ReadOnlyReactivePropertySlim<bool> InstallIsVisible { get; }

        public ReactivePropertySlim<PackageSource?> SelectedSource { get; } = new();

        public ObservableCollection<PackageSource> PackageSources { get; } = new();

        public ReactivePropertySlim<Package?> SelectedItem { get; } = new();

        public ObservableCollection<Package> Items { get; } = new();

        public ReactiveCommand OpenHomePage { get; } = new();

        public ReactiveProperty<PackageVersion?> SelectedVersion { get; }

        public ReactiveCommand Install { get; } = new();

        public ReactiveCommand CancelChange { get; } = new();

        public Task LoadTask { get; }

        private void ReloadItems()
        {
            Items.Clear();
            foreach (var item in _loadedItems!.Skip((Page.Value - 1) * 20).Take(20))
            {
                Items.Add(item);
            }
        }
    }
}