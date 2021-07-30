using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Avalonia.Controls;

using BEditor.Data;
using BEditor.Data.Property;
using BEditor.Models;
using BEditor.Properties;

using Microsoft.Extensions.Logging;

using Reactive.Bindings;

namespace BEditor.ViewModels.Dialogs
{
    public class CreateProjectPackageViewModel
    {
        public record TreeItem(string Text, string Tip);

        private readonly Project _project;

        public CreateProjectPackageViewModel()
        {
            OpenFolderDialog.Subscribe(OpenFolder);

            _project = AppModel.Current.Project;
            Folder.Value = _project.DirectoryName;
            Name.Value = _project.Name;
            Create = new();

            Create.Subscribe(async () =>
            {
                var msg = AppModel.Current.Message;
                try
                {
                    IsLoading.Value = true;
                    if (!await Task.Run(() => ProjectPackage.CreateFromProject(_project, Path.Combine(Folder.Value, Name.Value) + ".beproj")))
                    {
                        await msg.DialogAsync(Strings.FailedToPackProject, IMessage.IconType.Error);
                    }
                }
                catch (Exception e)
                {
                    await msg.DialogAsync(Strings.FailedToPackProject, IMessage.IconType.Error);
                    ServicesLocator.Current.Logger.LogError(e, Strings.FailedToPackProject);
                }
                finally
                {
                    IsLoading.Value = false;
                }
            });

            Task.Run(() =>
            {
                try
                {
                    IsLoading.Value = true;
                    foreach (var item in _project.GetAllChildren<FontProperty>()
                        .Distinct()
                        .Select(i => new TreeItem(i.Value.Name, i.Value.Filename)))
                    {
                        Fonts.Add(item);
                    }

                    foreach (var item in _project.GetAllChildren<FileProperty>()
                        .Distinct()
                        .Where(i => File.Exists(i.Value))
                        .Select(i => new TreeItem(Path.GetFileName(i.Value), i.Value)))
                    {
                        Files.Add(item);
                    }

                    foreach (var item in _project.FindDependentPlugins()
                        .Select(i => new TreeItem(
                            i.PluginName + "  " + i.GetType().Assembly.GetName()?.Version?.ToString(3) ?? string.Empty,
                            string.Empty)))
                    {
                        Plugins.Add(item);
                    }
                }
                finally
                {
                    IsLoading.Value = false;
                }
            });
        }

        public ObservableCollection<TreeItem> Fonts { get; } = new();

        public ObservableCollection<TreeItem> Files { get; } = new();

        public ObservableCollection<TreeItem> Plugins { get; } = new();

        public ReactivePropertySlim<string> Name { get; } = new();

        public ReactivePropertySlim<string> Folder { get; } = new();

        public ReactiveCommand OpenFolderDialog { get; } = new();

        public ReactivePropertySlim<bool> IsLoading { get; } = new(false);

        public AsyncReactiveCommand Create { get; }

        private async void OpenFolder()
        {
            var dialog = new OpenFolderDialog();
            var folder = await dialog.ShowAsync(App.GetMainWindow());

            if (Directory.Exists(folder))
            {
                Folder.Value = folder;
                var settings = BEditor.Settings.Default;

                settings.LastTimeFolder = folder;

                settings.Save();
            }
        }
    }
}
