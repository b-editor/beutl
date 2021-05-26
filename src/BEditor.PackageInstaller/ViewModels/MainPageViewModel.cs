using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

using Avalonia;

using BEditor.PackageInstaller.Models;

using Reactive.Bindings;

namespace BEditor.PackageInstaller.ViewModels
{
    public sealed class MainPageViewModel
    {
        public MainPageViewModel()
        {
            Task.Run(async () =>
            {
                IsLoading.Value = true;
                var packages = await ToRecordAsync(Program.JsonFile);
                AddRange(Installs, packages.Where(i => i.Type is PackageChangeType.Install));
                AddRange(Uninstalls, packages.Where(i => i.Type is PackageChangeType.Uninstall));
                AddRange(Updates, packages.Where(i => i.Type is PackageChangeType.Update));

                InstallsIsEmpty.Value = Installs.Count is 0;
                UninstallsIsEmpty.Value = Uninstalls.Count is 0;
                UpdatesIsEmpty.Value = Updates.Count is 0;

                IsLoading.Value = false;
            });
        }

        public ObservableCollection<PackageChange> Installs { get; } = new();

        public ObservableCollection<PackageChange> Uninstalls { get; } = new();

        public ObservableCollection<PackageChange> Updates { get; } = new();

        public ReactivePropertySlim<bool> IsLoading { get; } = new(false);

        public ReactivePropertySlim<bool> InstallsIsEmpty { get; } = new(true);

        public ReactivePropertySlim<bool> UninstallsIsEmpty { get; } = new(true);

        public ReactivePropertySlim<bool> UpdatesIsEmpty { get; } = new(true);

        private static void AddRange<T>(ObservableCollection<T> list, IEnumerable<T> enumerable)
        {
            foreach (var item in enumerable)
            {
                list.Add(item);
            }
        }

        private static async Task<PackageChange[]> ToRecordAsync(string file)
        {
            await using var stream = new FileStream(file, FileMode.Open);
            using var document = await JsonDocument.ParseAsync(stream);

            return document.RootElement.EnumerateArray()
                .Select(i =>
                {
                    var mainAsm = i.GetProperty("main-assembly").GetString();
                    var name = i.GetProperty("name").GetString();
                    var author = i.GetProperty("author").GetString();
                    var version = i.GetProperty("version").GetString();
                    var typeStr = i.GetProperty("type").GetString();
                    var id = i.GetProperty("id").GetGuid();
                    var license = i.GetProperty("license").GetString();
                    var url = i.TryGetProperty("url", out var urlj) ? urlj.GetString() : null;

                    var type = typeStr switch
                    {
                        "install" => PackageChangeType.Install,
                        "uninstall" => PackageChangeType.Uninstall,
                        "update" => PackageChangeType.Update,
                        _ => PackageChangeType.Cancel,
                    };

                    if (name is null || author is null || version is null) type = PackageChangeType.Cancel;

                    return new PackageChange(id, name!, mainAsm!, author!, version!, license!, type, url);
                })
                .Where(i => i.Type is not PackageChangeType.Cancel)
                .ToArray();
        }
    }
}