using System;
using System.IO;
using System.Reactive.Linq;
using System.Runtime.Loader;
using System.Threading.Tasks;

using Avalonia.Controls;

using BEditor.Models;
using BEditor.Packaging;
using BEditor.Properties;
using BEditor.Views.DialogContent;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditor.ViewModels.ManagePlugins
{
    public sealed class CreatePluginPackageViewModel
    {
        public CreatePluginPackageViewModel()
        {
            PublishIsEnabled = AppModel.Current.ObserveProperty(i => i.User)
                .Select(i => i is not null)
                .ToReadOnlyReactivePropertySlim();

            PickDirectory.Subscribe(async () =>
            {
                var dialog = new OpenFolderDialog();
                if (await dialog.ShowAsync(App.GetMainWindow()) is var dir && Directory.Exists(dir))
                {
                    OutputDirectory.Value = dir;
                }
            });

            PickAssemblyFile.Subscribe(async () =>
            {
                var dialog = new OpenFileRecord
                {
                    Filters =
                    {
                        new(Strings.AssemblyFile, new[]
                        {
                            "dll"
                        })
                    }
                };

                if (await AppModel.Current.FileDialog.ShowOpenFileDialogAsync(dialog))
                {
                    AssemblyFile.Value = dialog.FileName;
                }
            });

            Create.Subscribe(async _ =>
            {
                var progress = new ProgressDialog();
                var mainAsm = Path.GetFileName(AssemblyFile.Value);
                await OutputAsync(Path.Combine(OutputDirectory.Value, Path.ChangeExtension(mainAsm, ".bepkg")), progress);
                progress.Close();
            },
            async e =>
            {
                App.Logger.LogError(e, Strings.CouldNotCreatePackage);
                await AppModel.Current.Message.DialogAsync(Strings.CouldNotCreatePackage + $"\nmessage: {e.Message}");
            });

            Publish.Where(_ => PublishIsEnabled.Value)
                .Subscribe(async _ =>
                {
                    var auth = AppModel.Current.User;
                    if (auth.IsExpired()) await auth.RefreshAuthAsync();

                    var tmp = Path.GetTempFileName();
                    var progress = new ProgressDialog();
                    var service = AppModel.Current.ServiceProvider.GetRequiredService<IRemotePackageProvider>();

                    await OutputAsync(tmp, progress);

                    progress.IsIndeterminate.Value = true;
                    await service.UploadAsync(auth, tmp);

                    progress.Close();
                });
        }

        // Infomation
        public ReactivePropertySlim<string> Name { get; } = new(string.Empty);

        public ReactivePropertySlim<string> WebSite { get; } = new(string.Empty);

        public ReactivePropertySlim<string> DescriptionShort { get; } = new(string.Empty);

        public ReactivePropertySlim<string> Description { get; } = new(string.Empty);

        public ReactivePropertySlim<string> Tag { get; } = new(string.Empty);

        public ReactivePropertySlim<string> Id { get; } = new(string.Empty);

        public ReactivePropertySlim<string> License { get; } = new(string.Empty);

        // Version
        public ReactivePropertySlim<string> UpdateNote { get; } = new(string.Empty);

        public ReactivePropertySlim<string> UpdateNoteShort { get; } = new(string.Empty);

        // Output
        public ReactivePropertySlim<string> OutputDirectory { get; } = new(string.Empty);

        public ReactiveCommand PickDirectory { get; } = new();

        public ReactivePropertySlim<string> AssemblyFile { get; } = new(string.Empty);

        public ReactiveCommand PickAssemblyFile { get; } = new();

        public ReactiveCommand Create { get; } = new();

        // Publish
        public ReadOnlyReactivePropertySlim<bool> PublishIsEnabled { get; }

        public ReactiveCommand Publish { get; } = new();

        private async Task OutputAsync(string filename, ProgressDialog progress)
        {
            if (!File.Exists(AssemblyFile.Value))
            {
                await AppModel.Current.Message.DialogAsync(Strings.FileNotFound + $"\n{AssemblyFile.Value}");
                return;
            }
            if (!Guid.TryParse(Id.Value, out var id))
            {
                await AppModel.Current.Message.DialogAsync(Strings.InvalidIdCanUseGUIDAndUUID);
                return;
            }

            _ = progress.ShowDialog(App.GetMainWindow());
            var mainAsm = Path.GetFileName(AssemblyFile.Value);
            var asmName = AssemblyLoadContext.GetAssemblyName(AssemblyFile.Value);
            var ver = asmName.Version?.ToString(3) ?? "0.0.0";

            await PackageFile.CreatePackageAsync(
                AssemblyFile.Value,
                filename,
                new()
                {
                    MainAssembly = mainAsm,
                    Name = Name.Value,
                    Author = AppModel.Current.User?.User?.DisplayName ?? string.Empty,
                    HomePage = WebSite.Value,
                    DescriptionShort = DescriptionShort.Value,
                    Description = Description.Value,
                    Tag = Tag.Value,
                    Id = id,
                    License = License.Value,
                    Versions = new PackageVersion[]
                    {
                            new()
                            {
                                Version = ver,
                                UpdateNote = UpdateNote.Value,
                                UpdateNoteShort = UpdateNoteShort.Value,
                                ReleaseDateTime = DateTime.Now,
                            }
                    }
                },
                progress);
        }
    }
}