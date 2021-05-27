using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using Avalonia.Controls;

using BEditor.Data;
using BEditor.Models;
using BEditor.Properties;
using BEditor.Views.DialogContent;

using Reactive.Bindings;

namespace BEditor.ViewModels.DialogContent
{
    public class CreateProjectViewModel
    {
        private const int WIDTH = 1920;
        private const int HEIGHT = 1080;
        private const int FRAMERATE = 30;
        private const int SAMLINGRATE = 44100;

        public CreateProjectViewModel()
        {
            if (!Directory.Exists(BEditor.Settings.Default.LastTimeFolder))
            {
                BEditor.Settings.Default.LastTimeFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            }

            Folder = new(BEditor.Settings.Default.LastTimeFolder);

            OpenFolerDialog.Subscribe(OpenFolder);
            Create.Subscribe(CreateCoreAsync);
            Name = new ReactiveProperty<string>(GetNewName())
                .SetValidateNotifyError(name =>
                {
                    if (Directory.Exists(Path.Combine(Folder.Value, name)))
                    {
                        return Strings.ThisNameAlreadyExists;
                    }

                    return null;
                });
        }

        public ReactiveProperty<uint> Width { get; } = new(WIDTH);

        public ReactiveProperty<uint> Height { get; } = new(HEIGHT);

        public ReactiveProperty<uint> Framerate { get; } = new(FRAMERATE);

        public ReactiveProperty<uint> Samplingrate { get; } = new(SAMLINGRATE);

        public ReactiveProperty<string> Name { get; } = new();

        public ReactiveProperty<string> Folder { get; }

        public ReactiveCommand OpenFolerDialog { get; } = new();

        public AsyncReactiveCommand Create { get; } = new();

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

        private async Task CreateCoreAsync()
        {
            var app = AppModel.Current;
            var settings = BEditor.Settings.Default;
            var project = new Project(
                (int)Width.Value,
                (int)Height.Value,
                (int)Framerate.Value,
                (int)Samplingrate.Value,
                app,
                Path.Combine(Folder.Value, Name.Value, Name.Value) + ".bedit");

            var dialog = new ProgressDialog
            {
                IsIndeterminate = { Value = true }
            };
            dialog.Show(App.GetMainWindow());
            await TypeEarlyInitializer.AllInitializeAsync();

            await Task.Run(async () =>
            {
                project.Load();

                {
                    await project.SaveAsync();

                    var fullpath = Path.Combine(project.DirectoryName!, project.Name + ".bedit");

                    settings.RecentFiles.Remove(fullpath);

                    settings.RecentFiles.Add(fullpath);
                }

                app.AppStatus = Status.Edit;

                await settings.SaveAsync();
            });

            app.Project = project;

            dialog.Close();
        }

        private string GetNewName()
        {
            var dirs = Directory.GetDirectories(Folder.Value, "Project*");

            var reg = new Regex(@"^Project([\d]+)\z");

            var values = dirs.Select(i => new DirectoryInfo(i))
                .Select(i => i.Name)
                .Where(i => reg.IsMatch(i))
                .Select(i => reg.Match(i))
                .Select(i => int.Parse(i.Groups[1].Value))
                .ToArray();
            if (values.Length is 0) return "Project1";

            return "Project" + (values.Max() + 1);
        }
    }
}