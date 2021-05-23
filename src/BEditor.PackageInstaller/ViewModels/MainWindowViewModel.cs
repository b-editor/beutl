using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using BEditor.PackageInstaller.Models;

using Reactive.Bindings;

namespace BEditor.PackageInstaller.ViewModels
{
    public sealed class MainWindowViewModel
    {
        public MainWindowViewModel()
        {
            Task.Run(async () =>
            {
                IsLoading.Value = true;
                Packages.AddRangeOnScheduler(await ToRecordAsync(Environment.GetCommandLineArgs()[0]));
                IsLoading.Value = false;
            });
        }

        public ReactiveCollection<PackageChange> Packages { get; } = new();

        public ReactivePropertySlim<bool> IsLoading { get; } = new(false);

        private static async Task<IEnumerable<PackageChange>> ToRecordAsync(string file)
        {
            await using var stream = new FileStream(file, FileMode.Open);
            using var document = await JsonDocument.ParseAsync(stream);

            return document.RootElement.EnumerateArray()
                .Select(i =>
                {
                    var name = i.GetProperty("name").GetString();
                    var author = i.GetProperty("author").GetString();
                    var version = i.GetProperty("version").GetString();
                    var typeStr = i.GetProperty("type").GetString();
                    var id = i.GetProperty("id").GetGuid();
                    var url = i.TryGetProperty("url", out var urlj) ? urlj.GetString() : null;

                    var type = typeStr switch
                    {
                        "install" => PackageChangeType.Install,
                        "uninstall" => PackageChangeType.Uninstall,
                        "update" => PackageChangeType.Update,
                        _ => PackageChangeType.Cancel,
                    };

                    if (name is null || author is null || version is null) type = PackageChangeType.Cancel;

                    return new PackageChange(id, name!, author!, version!, type, url);
                })
                .Where(i => i.Type is PackageChangeType.Cancel);
        }
    }
}
