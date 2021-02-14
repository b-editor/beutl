using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core;
using BEditor.Core.Data;
using BEditor.Core.Data.Property;
using BEditor.Core.Extensions;
using BEditor.Core.Properties;
using Service = BEditor.Core.Service.Services;

using Microsoft.Win32;

using Reactive.Bindings;
using BEditor.Core.Service;
using System.IO;
using BEditor.Views.MessageContent;
using BEditor.Views;
using BEditor.ViewModels;

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
                    if (Service.FileDialogService is null) throw new InvalidOperationException();

                    var record = new SaveFileRecord
                    {
                        DefaultFileName = (p!.Name is not null) ? p.Name + ".bedit" : "新しいプロジェクト.bedit",
                        Filters =
                        {
                            new(Resources.ProjectFile, new FileExtension[] { new("bedit") }),
                            new(Resources.JsonFile, new FileExtension[] { new("json") }),
                        }
                    };

                    var mode = SerializeMode.Binary;

                    if (Service.FileDialogService.ShowSaveFileDialog(record))
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
                .Subscribe(p =>
                {
                    MainWindowViewModel.Current.IsLoading.Value = true;
                    Task.Run(() =>
                    {
                        p!.Save();

                        MainWindowViewModel.Current.IsLoading.Value = false;
                    });
                });

            Open.Select(_ => AppData.Current).Subscribe(async app =>
            {
                var dialog = new OpenFileDialog()
                {
                    Filter = $"{Resources.ProjectFile}|*.bedit|{Resources.BackupFile}|*.backup|{Resources.JsonFile}|*.json",
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
                    catch
                    {
                        Debug.Assert(false);
                        Message.Snackbar(string.Format(Resources.FailedToLoad, "Project"));
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

        public static async Task DirectOpen(string filename)
        {
            var app = AppData.Current;
            app.Project?.Unload();
            var project = new Project(filename);

            await Task.Run(() =>
            {
                project.Load();

                app.Project = project;
                app.AppStatus = Status.Edit;

                Settings.Default.MostRecentlyUsedList.Remove(filename);
                Settings.Default.MostRecentlyUsedList.Add(filename);
            });
        }
    }
}
