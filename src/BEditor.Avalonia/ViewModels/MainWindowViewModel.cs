using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;

using Avalonia.Controls;

using BEditor.Data;
using BEditor.Models;
using BEditor.Properties;

using Microsoft.Extensions.Logging;

using Reactive.Bindings;

namespace BEditor.ViewModels
{
    public class MainWindowViewModel
    {
        public static readonly MainWindowViewModel Current = new();

        public MainWindowViewModel()
        {
            Open.Subscribe(async() =>
            {
                var dialog = new OpenFileRecord
                {
                    Filters =
                    {
                        new(Strings.ProjectFile, new FileExtension[] { new("bedit") }),
                        new(Strings.BackupFile, new FileExtension[] { new("backup") }),
                    }
                };
                var service = AppModel.Current.FileDialog;

                if (await service.ShowOpenFileDialogAsync(dialog))
                {
                    //NoneDialog? ndialog = null;
                    try
                    {
                        //var loading = new Loading()
                        //{
                        //    IsIndeterminate = { Value = true }
                        //};
                        //ndialog = new NoneDialog(loading)
                        //{
                        //    Owner = App.Current.MainWindow
                        //};
                        //ndialog.Show();

                        await DirectOpenAsync(dialog.FileName);
                    }
                    catch (Exception e)
                    {
                        Debug.Fail(string.Empty);

                        var msg = string.Format(Strings.FailedToLoad, "Project");
                        //AppData.Current.Message.Snackbar(msg);

                        App.Logger?.LogError(e, msg);
                    }
                    finally
                    {
                        //ndialog?.Close();
                    }
                }
            });
            Close.Select(_ => AppModel.Current)
                .Where(app => app.Project is not null)
                .Subscribe(app =>
                {
                    app.Project?.Unload();
                    app.Project = null;
                    app.AppStatus = Status.Idle;
                });
        }

        public ReactiveCommand Open { get; } = new();
        public ReactiveCommand Close { get; } = new();

        public static async ValueTask DirectOpenAsync(string filename)
        {
            var app = AppModel.Current;
            app.Project?.Unload();
            var project = Project.FromFile(filename, app);

            if (project is null) return;

            await Task.Run(() =>
            {
                project.Load();

                app.Project = project;
                app.AppStatus = Status.Edit;

                BEditor.Settings.Default.RecentlyUsedFiles.Remove(filename);
                BEditor.Settings.Default.RecentlyUsedFiles.Add(filename);
            });
        }
    }
}
