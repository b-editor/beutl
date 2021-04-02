using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Media;

using BEditor.Command;
using BEditor.Data;
using BEditor.Models;
using BEditor.Properties;
using BEditor.Views.DialogContent;

using Microsoft.Extensions.Logging;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditor.ViewModels
{
    public class MainWindowViewModel
    {
        public static readonly MainWindowViewModel Current = new();

        public MainWindowViewModel()
        {
            Open.Subscribe(async () =>
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
                    EmptyDialog? ndialog = null;
                    try
                    {
                        var loading = new Loading
                        {
                            IsIndeterminate = { Value = true }
                        };
                        ndialog = new EmptyDialog(loading);
                        ndialog.Show(BEditor.App.GetMainWindow());

                        await DirectOpenAsync(dialog.FileName);
                    }
                    catch (Exception e)
                    {
                        Debug.Fail(string.Empty);

                        var msg = string.Format(Strings.FailedToLoad, Strings.Project);
                        //AppData.Current.Message.Snackbar(msg);

                        BEditor.App.Logger?.LogError(e, msg);
                    }
                    finally
                    {
                        ndialog?.Close();
                    }
                }
            });

            Save.Select(_ => AppModel.Current.Project)
                .Where(p => p is not null)
                .Subscribe(async p => await p!.SaveAsync());

            SaveAs.Select(_ => AppModel.Current.Project)
                .Where(p => p is not null)
                .Subscribe(async p =>
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

                    if (await AppModel.Current.FileDialog.ShowSaveFileDialogAsync(record))
                    {
                        if (Path.GetExtension(record.FileName) is ".json")
                        {
                            mode = SerializeMode.Json;
                        }

                        await p.SaveAsync(record.FileName, mode);
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

            IsOpened.Subscribe(_ => CommandManager.Default.Clear());

            Previewer = new(IsOpened);
        }

        public ReactiveCommand Open { get; } = new();
        public ReactiveCommand Save { get; } = new();
        public ReactiveCommand SaveAs { get; } = new();
        public ReactiveCommand Close { get; } = new();
        public ReadOnlyReactivePropertySlim<bool> IsOpened { get; } = AppModel.Current
            .ObserveProperty(p => p.Project)
            .Select(p => p is not null)
            .ToReadOnlyReactivePropertySlim();
        public PreviewerViewModel Previewer { get; }
        public AppModel App => AppModel.Current;

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
