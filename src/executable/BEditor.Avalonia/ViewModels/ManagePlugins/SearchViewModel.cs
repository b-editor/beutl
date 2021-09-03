using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

using BEditor.Models;
using BEditor.Packaging;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;

using Setting = BEditor.Settings;

namespace BEditor.ViewModels.ManagePlugins
{
    public sealed class SearchViewModel
    {
        private readonly HttpClient _client;
        private readonly List<PackageSource> _packageSources = new();
        private Package[]? _loadedItems;

        public SearchViewModel()
        {
            _client = AppModel.Current.ServiceProvider.GetRequiredService<HttpClient>();

            SearchText.Subscribe(str =>
            {
                if (_loadedItems == null)
                    return;

                if (string.IsNullOrWhiteSpace(str))
                {
                    ReloadItems();
                    return;
                }

                var regices = SearchService.CreateRegices(str);

                Items.Clear();
                Items.AddRangeOnScheduler(_loadedItems!
                    .Where(i => SearchService.IsMatch(regices, i.Name)
                    || SearchService.IsMatch(regices, i.Description)
                    || SearchService.IsMatch(regices, i.Tag)));
            });

            SelectedItem.Subscribe(async i =>
            {
                if (i == null)
                    return;

                await Navigate.ExecuteAsync(i).ConfigureAwait(false);
                SelectedItem.Value = null;
            });

            Task.Run(async () =>
            {
                foreach (var item in Setting.Default.PackageSources)
                {
                    var repos = await item.ToRepositoryAsync(_client).ConfigureAwait(false);
                    if (repos is null) continue;
                    _packageSources.Add(repos);
                }

                _loadedItems = _packageSources.SelectMany(i => i.Packages).ToArray();
                ReloadItems();
                IsLoaded.Value = false;
            });
        }

        public ReactivePropertySlim<bool> IsLoaded { get; } = new(true);

        public ReactiveProperty<string> SearchText { get; } = new();

        public ReactiveCollection<Package> Items { get; } = new();

        public ReactivePropertySlim<Package?> SelectedItem { get; } = new();

        public AsyncReactiveCommand<Package> Navigate { get; } = new();

        private void ReloadItems()
        {
            Items.ClearOnScheduler();
            Items.AddRangeOnScheduler(_loadedItems!);
        }
    }
}
