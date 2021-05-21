using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Pack = BEditor.Package.Package;
using Setting = BEditor.Settings;

using Reactive.Bindings;
using System.Net.Http;
using BEditor.Models;
using Microsoft.Extensions.DependencyInjection;
using BEditor.Package;
using System.Reactive.Linq;
using System.Collections.ObjectModel;

namespace BEditor.ViewModels.ManagePlugins
{
    public class LibraryViewModel
    {
        private readonly HttpClient _client;

        public LibraryViewModel()
        {
            _client = AppModel.Current.ServiceProvider.GetRequiredService<HttpClient>();
            Task.Run(async () =>
            {
                foreach (var item in Setting.Default.Repositories)
                {
                    var repos = await item.ToRepositoryAsync(_client);
                    if (repos is null) continue;
                    Repositories.Add(repos);
                }

                SelectedRepos.Value = Repositories[0];
                IsLoaded.Value = false;
            });
        }

        public ReactiveCommand Search { get; } = new();

        public ReactivePropertySlim<string> SearchText { get; } = new(string.Empty);

        public ReactivePropertySlim<bool> IsLoaded { get; } = new(true);

        public ReactiveProperty<Repository> SelectedRepos { get; } = new();

        public ObservableCollection<Repository> Repositories { get; } = new();

        public ObservableCollection<Pack> Items { get; } = new();
    }
}
