using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Data;
using BEditor.Core.Extensions;
using BEditor.Core.Properties;
using BEditor.Core.Service;

using Microsoft.WindowsAPICodePack.Dialogs;

using Reactive.Bindings;

namespace BEditor.Models
{
    public class ProjectModel
    {
        public static readonly ProjectModel Current = new();

        private ProjectModel()
        {
            SaveAs.Where(_ => AppData.Current.Project is not null)
                .Select(_ => AppData.Current.Project)
                .Subscribe(p => p.SaveAs());

            Save.Where(_ => AppData.Current.Project is not null)
                .Select(_ => AppData.Current.Project)
                .Subscribe(p => p.Save());

            Open.Select(_ => AppData.Current).Subscribe(app =>
            {
                var dialog = new CommonOpenFileDialog()
                {
                    Filters =
                    {
                        new(Resources.ProjectFile, "bedit"),
                        new(Resources.BackupFile, "backup"),
                        new(Resources.JsonFile, "json"),
                    },
                    RestoreDirectory = true
                };

                if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
                {
                    try
                    {
                        app.Project?.Unloaded();
                        var project = new Project(dialog.FileName);
                        project.Loaded();
                        app.Project = project;
                        app.AppStatus = Status.Edit;

                        Settings.Default.MostRecentlyUsedList.Remove(dialog.FileName);
                        Settings.Default.MostRecentlyUsedList.Add(dialog.FileName);
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

        public event EventHandler CreateEvent;

        public ReactiveCommand SaveAs { get; } = new();
        public ReactiveCommand Save { get; } = new();
        public ReactiveCommand Open { get; } = new();
        public ReactiveCommand Close { get; } = new();
        public ReactiveCommand Create { get; } = new();
    }
}
