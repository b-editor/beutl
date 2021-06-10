using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Threading;

using BEditor.Data;
using BEditor.ViewModels.DialogContent;
using BEditor.Views;
using BEditor.Views.DialogContent;

namespace BEditor.Models
{
    public static class ArgumentsContext
    {
        /// <summary>
        /// 引数で指定したコマンドを実行したかの値を取得します。
        /// </summary>
        public static bool IsExecuted { get; private set; }

        public static async ValueTask ExecuteAsync()
        {
            if (IsExecuted) return;

            await Dispatcher.UIThread.InvokeAsync(() => GetAction(Environment.GetCommandLineArgs()).Invoke());

            IsExecuted = true;
        }

        private static Func<ValueTask> GetAction(string[] args)
        {
            if (args.Length is 0)
            {
                return () => default;
            }
            else if (args[0] is "settings")
            {
                return async () => await new SettingsWindow().ShowDialog(App.GetMainWindow());
            }
            else if (args[0] is "new")
            {
                return () => NewProjectAsync();
            }
            else if (Path.GetExtension(args[0]) is ".bedit")
            {
                return () => OpenProjectAsync(args[0]);
            }
            else if (args.Length is 2)
            {
                if (Path.GetExtension(args[1]) is ".bedit") return () => OpenProjectAsync(args[1]);
                else if (args[1] is "settings")
                {
                    return async () => await new SettingsWindow().ShowDialog(App.GetMainWindow());
                }
                else if (args[1] is "new")
                {
                    return () => NewProjectAsync();
                }
                else return () => default;
            }
            else
            {
                return () => default;
            }
        }

        private static async ValueTask OpenProjectAsync(string filename)
        {
            if (App.GetMainWindow() is StartWindow start)
            {
                var main = new MainWindow();
                App.SetMainWindow(main);
                main.Show();
                start.Close();
            }

            var app = AppModel.Current;
            app.Project?.Unload();
            var project = Project.FromFile(filename, app);

            if (project is null) return;

            await Task.Run(async () =>
            {
                await TypeEarlyInitializer.AllInitializeAsync();
                project.Load();

                app.Project = project;
                app.AppStatus = Status.Edit;

                Settings.Default.RecentFiles.Remove(filename);
                Settings.Default.RecentFiles.Add(filename);
            });
        }

        private static async ValueTask NewProjectAsync()
        {
            var window = App.GetMainWindow();
            if (window is StartWindow)
            {
                var dialog = new CreateProject { DataContext = new CreateProjectViewModel() };
                App.SetMainWindow(dialog);
                dialog.Show();
                window.Close();

                dialog.Closing += Dialog_Closing;
            }
            else
            {
                var dialog = new CreateProject { DataContext = new CreateProjectViewModel() };
                await dialog.ShowDialog(window);
            }
        }

        private static void Dialog_Closing(object? sender, CancelEventArgs e)
        {
            if (sender is Window window)
            {
                var main = new MainWindow();
                App.SetMainWindow(main);
                main.Show();

                window.Closing -= Dialog_Closing;
            }
        }
    }
}