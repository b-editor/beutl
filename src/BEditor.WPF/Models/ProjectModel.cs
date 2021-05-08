

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;

using BEditor.Data;
using BEditor.Properties;
using BEditor.ViewModels;
using BEditor.Views;
using BEditor.Views.MessageContent;

using Microsoft.Extensions.Logging;
using Microsoft.Win32;

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
                .Subscribe(p =>
                {
                    var record = new SaveFileRecord
                    {
                        DefaultFileName = (p!.Name is not null) ? p.Name + ".bedit" : "新しいプロジェクト.bedit",
                        Filters =
                        {
                            new(Strings.ProjectFile, new FileExtension[] { new("bedit") }),
                        }
                    };

                    var mode = SerializeMode.Binary;

                    if (AppData.Current.FileDialog.ShowSaveFileDialog(record))
                    {
                        if (Path.GetExtension(record.FileName) is ".json")
                        {
                            mode = SerializeMode.Json;
                        }

                        p.Save(record.FileName, mode);
                    }
                });

            Save.Where(_ => AppData.Current.Project is not null)
                .Select(_ => AppData.Current.Project)
                .Subscribe(async p =>
                {
                    MainWindowViewModel.Current.IsLoading.Value = true;
                    await Task.Run(() =>
                    {
                        p!.Save();

                        MainWindowViewModel.Current.IsLoading.Value = false;
                    });
                });

            Open.Select(_ => AppData.Current).Subscribe(async app =>
            {
                var dialog = new OpenFileDialog()
                {
                    Filter = $"{Strings.ProjectFile}|*.bedit|{Strings.BackupFile}|*.backup",
                    RestoreDirectory = true
                };

                if (dialog.ShowDialog() ?? false)
                {
                    NoneDialog? ndialog = null;
                    try
                    {
                        var loading = new Loading()
                        {
                            IsIndeterminate = { Value = true }
                        };
                        ndialog = new NoneDialog(loading)
                        {
                            Owner = App.Current.MainWindow
                        };
                        ndialog.Show();

                        await DirectOpen(dialog.FileName);
                    }
                    catch (Exception e)
                    {
                        Debug.Assert(false);

                        var msg = string.Format(Strings.FailedToLoad, Strings.Project);
                        AppData.Current.Message.Snackbar(msg);

                        App.Logger?.LogError(e, msg);
                    }
                    finally
                    {
                        ndialog?.Close();
                    }
                }
            });

            Close.Select(_ => AppData.Current)
                .Where(app => app.Project is not null)
                .Subscribe(app =>
            {
                app.Project?.Unload();
                app.Project = null;
                app.AppStatus = Status.Idle;
            });

            Create.Subscribe(_ =>
            {
                AppData.Current.Project?.Unload();
                CreateEvent?.Invoke(this, EventArgs.Empty);
            });
        }

        public event EventHandler? CreateEvent;

        public ReactiveCommand SaveAs { get; } = new();
        public ReactiveCommand Save { get; } = new();
        public ReactiveCommand Open { get; } = new();
        public ReactiveCommand Close { get; } = new();
        public ReactiveCommand Create { get; } = new();

        public static async ValueTask DirectOpen(string filename)
        {
            var app = AppData.Current;
            app.Project?.Unload();
            var project = Project.FromFile(filename, app);

            if (project is null) return;

            await Task.Run(() =>
            {
                project.Load();

                app.Project = project;
                app.AppStatus = Status.Edit;

                Settings.Default.RecentFiles.Remove(filename);
                Settings.Default.RecentFiles.Add(filename);
            });
        }
    }
}