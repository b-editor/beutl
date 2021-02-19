using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

using BEditor.Data;
using BEditor.Models;
using BEditor.Properties;
using BEditor.Views;
using BEditor.Views.MessageContent;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;

namespace BEditor.ViewModels.StartWindowControl
{
    public class ProjectsControlViewModel : BasePropertyChanged
    {
        public ProjectsControlViewModel()
        {
            CountIsZero = new(!Settings.Default.MostRecentlyUsedList
                .Where(i => File.Exists(i))
                .Any());
            CountIsNotZero = CountIsZero.Select(i => !i).ToReadOnlyReactiveProperty();

            Projects = new(Settings.Default.MostRecentlyUsedList
                .Where(i => File.Exists(i))
                .Select(i => new ProjectItem(Path.GetFileNameWithoutExtension(i), i, Click, Remove)));

            Click.Subscribe(async ProjectItem =>
            {
                ProjectItem.IsLoading.Value = true;

                var app = AppData.Current;
                app.Project?.Unload();
                var project = Project.FromFile(ProjectItem.Path, app);

                if (project is null) return;

                await Task.Run(() =>
                {
                    project.Load();

                    app.Project = project;
                    app.AppStatus = Status.Edit;

                    Settings.Default.MostRecentlyUsedList.Remove(ProjectItem.Path);
                    Settings.Default.MostRecentlyUsedList.Add(ProjectItem.Path);

                    App.Current.Dispatcher.Invoke(() =>
                    {
                        var win = new MainWindow();
                        App.Current.MainWindow = win;
                        win.Show();

                        Close?.Invoke(this, EventArgs.Empty);
                    });
                });
            });

            Create.Subscribe(() =>
            {
                ProjectModel.Current.Create.Execute();

                if (AppData.Current.Project is not null)
                {
                    var win = new MainWindow();
                    App.Current.MainWindow = win;
                    win.Show();

                    Close?.Invoke(this, EventArgs.Empty);
                }
            });
            Remove.Subscribe(item =>
            {
                Projects.Remove(item);
                Settings.Default.MostRecentlyUsedList.Remove(item.Path);

                if (Projects.Count is 0)
                {
                    CountIsZero.Value = true;
                }
            });
            Add.Subscribe(async () =>
            {
                await using var prov = AppData.Current.Services.BuildServiceProvider();
                var record = new OpenFileRecord()
                {
                    Filters =
                    {
                        new(Resources.ProjectFile, new FileExtension[] { new("bedit") })
                    }
                };


                if (prov.GetService<IFileDialogService>()?.ShowOpenFileDialog(record) ?? false)
                {
                    var f = Projects.Count is 0;
                    Projects.Insert(0, new ProjectItem(Path.GetFileNameWithoutExtension(record.FileName), record.FileName, Click, Remove));
                    Settings.Default.MostRecentlyUsedList.Add(record.FileName);

                    if (f)
                    {
                        CountIsZero.Value = false;
                    }
                }
            });
        }

        public ReactiveCommand<ProjectItem> Click { get; } = new();
        public ReactiveCommand<ProjectItem> Remove { get; } = new();
        public ReactiveCommand Create { get; } = new();
        public ReactiveCommand Add { get; } = new();
        public ObservableCollection<ProjectItem> Projects { get; }
        public ReactiveProperty<bool> CountIsZero { get; }
        public ReadOnlyReactiveProperty<bool> CountIsNotZero { get; }

        public event EventHandler? Close;

        public record ProjectItem(string Name, string Path, ICommand Command, ICommand Remove)
        {
            public string? ThumbnailPath
                => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path)!, "thumbnail.png") is var p && File.Exists(p) ? p : null;
            public ReactiveProperty<bool> IsLoading { get; } = new(false);
        }
    }
}
