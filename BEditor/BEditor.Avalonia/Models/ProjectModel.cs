using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;

using Avalonia.Controls;

using BEditor.Core.Data;
using BEditor.Core.Extensions;
using BEditor.Core.Properties;
using BEditor.Core.Service;
using BEditor.Views;

using Reactive.Bindings;

namespace BEditor.Models
{
    public class ProjectModel
    {
        public static readonly ProjectModel Current = new();

        private ProjectModel()
        {
            SaveAs.Where(_ => AppData.Current.Project is not null)
                .Select(_ => AppData.Current.Project!)
                .Subscribe(p => p.SaveAs());

            Save.Where(_ => AppData.Current.Project is not null)
                .Select(_ => AppData.Current.Project!)
                .Subscribe(p => p.Save());

            Open.Select(_ => AppData.Current).Subscribe(async app =>
            {
                var dialog = new OpenFileDialog()
                {
                    Filters =
                    {
                        new() { Name = Resources.ProjectFile, Extensions = { "bedit" } },
                        new() { Name = Resources.BackupFile, Extensions = { "backup" } },
                        new() { Name = Resources.JsonFile, Extensions = { "json" } },
                    },
                };

                if (await dialog.ShowAsync(MainWindow.Current) is var result and { })
                {
                    try
                    {
                        var filename = result.FirstOrDefault();

                        if (filename is null) return;

                        app.Project?.Unloaded();
                        var project = new Project(filename);
                        project.Loaded();
                        app.Project = project;
                        app.AppStatus = Status.Edit;

                        Settings.Default.MostRecentlyUsedList.Remove(filename);
                        Settings.Default.MostRecentlyUsedList.Add(filename);
                    }
                    catch
                    {
                        Debug.Assert(false);
                        Message.Snackbar(string.Format(Resources.FailedToLoad, "Project"));
                    }
                }
            });

            Close.Select(_ => AppData.Current)
                .Where(app => app.Project is not null)
                .Subscribe(app =>
            {
                app.Project?.Unloaded();
                app.Project = null;
                app.AppStatus = Status.Idle;
            });

            Create.Subscribe(_ =>
            {
                AppData.Current.Project?.Unloaded();
                CreateEvent?.Invoke(this, EventArgs.Empty);
            });
        }

        public event EventHandler CreateEvent = delegate { };

        public ReactiveCommand SaveAs { get; } = new();
        public ReactiveCommand Save { get; } = new();
        public ReactiveCommand Open { get; } = new();
        public ReactiveCommand Close { get; } = new();
        public ReactiveCommand Create { get; } = new();
    }
}
