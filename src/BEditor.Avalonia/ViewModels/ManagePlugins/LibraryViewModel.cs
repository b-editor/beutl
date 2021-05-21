using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Reactive.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using BEditor.Models;
using BEditor.Package;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;

using Pack = BEditor.Package.Package;
using Setting = BEditor.Settings;

namespace BEditor.ViewModels.ManagePlugins
{
    public class LibraryViewModel
    {
        private readonly HttpClient _client;
        private Pack[]? _loadedItems;

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
                var str = SearchText.Value.ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(str))
                {
                    _loadedItems = SelectedSource.Value!.Packages;

                    ReloadItems();
                    Page.Value = 1;
                    Page.ForceNotify();

                    return;
                }

                var regexPattern = Regex.Replace(str, ".", m =>
                {
                    var s = m.Value;
                    if (s.Equals("?"))
                    {
                        return ".";
                    }
                    else if (s.Equals("*"))
                    {
                        return ".*";
                    }
                    else
                    {
                        return Regex.Escape(s);
                    }
                });
                var regex = new Regex(regexPattern.ToLowerInvariant());

                _loadedItems = SelectedSource.Value!.Packages
                    .Where(i => regex.IsMatch(i.Name.ToLowerInvariant()) ||
                        regex.IsMatch(i.Description.ToLowerInvariant()) ||
                        regex.IsMatch(i.Tag.ToLowerInvariant()))
                    .ToArray();

                ReloadItems();

                Page.Value = 1;
                Page.ForceNotify();
            });

            Task.Run(async () =>
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
        }

        public ReactiveCommand NextPage { get; } = new();

        public ReactiveCommand PrevPage { get; } = new();

        public ReadOnlyReactivePropertySlim<bool> ExistNextPage { get; }

        public ReadOnlyReactivePropertySlim<bool> ExistPrevPage { get; }

        public ReactiveProperty<int> Page { get; } = new(0);

        public ReactiveCommand Search { get; } = new();

        public ReactivePropertySlim<string> SearchText { get; } = new(string.Empty);

        public ReactivePropertySlim<bool> IsLoaded { get; } = new(true);

        public ReactiveProperty<PackageSource?> SelectedSource { get; } = new();

        public ObservableCollection<PackageSource> PackageSources { get; } = new();

        public ObservableCollection<Pack> Items { get; } = new();

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
