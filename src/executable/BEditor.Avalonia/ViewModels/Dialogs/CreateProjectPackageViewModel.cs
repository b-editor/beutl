using System;
using System.Collections.Generic;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Avalonia.Controls;

using BEditor.Data;
using BEditor.Models;
using BEditor.Properties;

using Microsoft.Extensions.Logging;

using Reactive.Bindings;

namespace BEditor.ViewModels.Dialogs
{
    public class CreateProjectPackageViewModel
    {
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
                    var package = ProjectPackage.FromProject(_project);
                    if (package is null)
                    {
                        await msg.DialogAsync(Strings.FailedToPackProject, IMessage.IconType.Error);
                        return;
                    }

                    package.Compress(Path.Combine(Folder.Value, Name.Value) + ".beproj");
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
        }

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
