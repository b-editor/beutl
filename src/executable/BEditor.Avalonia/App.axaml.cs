using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;

using BEditor.Data;
using BEditor.Models;
using BEditor.Models.ManagePlugins;
using BEditor.Plugin;
using BEditor.Primitive;
using BEditor.Properties;
using BEditor.Views;
using BEditor.Views.DialogContent;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BEditor
{
    public class App : Application
    {
        public static readonly ILogger Logger = AppModel.Current.LoggingFactory.CreateLogger<App>();
        public static readonly DispatcherTimer BackupTimer = new()
        {
            Interval = TimeSpan.FromMinutes(Settings.Default.BackUpInterval)
        };
        private static readonly string pluginsDir = Path.Combine(AppContext.BaseDirectory, "user", "plugins");

        public static void Shutdown(int exitCode)
        {
            ((IClassicDesktopStyleApplicationLifetime)Current.ApplicationLifetime).Shutdown(exitCode);
        }

        public static Window GetMainWindow()
        {
            if (Current.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                return desktop.MainWindow;
            }

            throw new Exception();
        }

        public static void SetMainWindow(Window window)
        {
            if (Current.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = window;
            }
        }

        public override void RegisterServices()
        {
            if (OperatingSystem.IsLinux())
            {
                AvaloniaLocator.CurrentMutable.Bind<IFontManagerImpl>().ToConstant(new CustomFontManagerImpl());
            }
            base.RegisterServices();
        }

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
            var baseuri = new Uri("avares://beditor/App.axaml");
            var style = new StyleInclude(baseuri)
            {
                Source = Settings.Default.UseDarkMode ? new("avares://beditor/Controls/DarkTheme.axaml") : new("avares://beditor/Controls/LightTheme.axaml")
            };

            Styles.Insert(0, style);
        }

        public override async void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                RegisterPrimitive();

                desktop.MainWindow = Settings.Default.ShowStartWindow ? new StartWindow() : new MainWindow();
                AppModel.Current.UIThread = SynchronizationContext.Current;

                CreateDirectory();

                await InitialPluginsAsync();

                AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

                RunBackup();

                desktop.Exit += Desktop_Exit;
            }

            base.OnFrameworkInitializationCompleted();
        }

        private void Desktop_Exit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
        {
            Settings.Default.Save();
            KeyBindingModel.Save();

            DirectoryManager.Default.Stop();

            BackupTimer.Stop();

            var app = AppModel.Current;

            app.RaiseExit();
            app.ServiceProvider.GetService<HttpClient>()?.Dispose();

            app.Project?.Unload();
            app.Project = null;

            if (PluginChangeSchedule.Uninstall.Count is not 0 || PluginChangeSchedule.UpdateOrInstall.Count is not 0)
            {
                var jsonfile = Path.Combine(AppContext.BaseDirectory, "package-install.json");
                PluginChangeSchedule.CreateJsonFile(jsonfile);

                if (OperatingSystem.IsWindows())
                {
                    Process.Start(new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "BEditor.PackageInstaller.exe"), jsonfile)
                    {
                        UseShellExecute = true
                    });
                }
                else if (OperatingSystem.IsLinux())
                {
                    Process.Start(new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "BEditor.PackageInstaller"), jsonfile)
                    {
                        UseShellExecute = true
                    });
                }
            }
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            AppModel.Current.Message.Snackbar(string.Format(Strings.ExceptionWasThrown, e.ExceptionObject.ToString()));

            Logger?.LogError(e.ExceptionObject as Exception, "UnhandledException was thrown.");
        }

        private static void CreateDirectory()
        {
            DirectoryManager.Default.Directories.Add(pluginsDir);

            DirectoryManager.Default.Run();
        }

        private static void RunBackup()
        {
            BackupTimer.Tick += (s, e) =>
            {
                Task.Run(() =>
                {
                    var proj = AppModel.Current.Project;
                    if (proj is not null && Settings.Default.AutoBackUp)
                    {
                        var dir = Path.Combine(proj.DirectoryName, "backup");
                        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                        proj.Save(Path.Combine(dir, DateTime.Now.ToString("HH:mm:ss").Replace(':', '_')) + ".backup");

                        var files = Directory.GetFiles(dir).Select(i => new FileInfo(i)).ToArray();
                        Array.Sort(files, (x, y) => y.LastWriteTime.CompareTo(x.LastWriteTime));
                        if (files.Length is > 10)
                        {
                            foreach (var file in files.Skip(10))
                            {
                                if (file.Exists) file.Delete();
                            }
                        }
                    }
                });
            };

            Settings.Default.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName is nameof(Settings.BackUpInterval))
                {
                    BackupTimer.Interval = TimeSpan.FromMinutes(Settings.Default.BackUpInterval);
                }
            };

            BackupTimer.Start();
        }

        private static void RegisterPrimitive()
        {
            foreach (var obj in PrimitiveTypes.EnumerateAllObjectMetadata())
            {
                ObjectMetadata.LoadedObjects.Add(obj);
            }

            foreach (var effect in PrimitiveTypes.EnumerateAllEffectMetadata())
            {
                EffectMetadata.LoadedEffects.Add(effect);
            }
        }

        private static async ValueTask InitialPluginsAsync()
        {
            PluginBuilder.Config = new PluginConfig(AppModel.Current);
            // ‚·‚×‚Ä
            var all = PluginManager.Default.GetNames();
            var app = AppModel.Current;

            try
            {
                PluginManager.Default.Load(all);
            }
            catch (AggregateException e)
            {
                var msg = string.Format(Strings.FailedToLoad, Strings.Plugins);
                Logger.LogError(e, msg);
                var sb = new StringBuilder(msg);

                foreach (var item in e.InnerExceptions)
                {
                    if (item is PluginException ex)
                    {
                        sb.Append('\n');
                        sb.Append("* ");
                        sb.Append(ex.PluginName);
                    }
                }

                await app.Message.DialogAsync(sb.ToString());
            }

            if (PluginManager.Default._tasks.Count is not 0)
            {
                var dialog = new ProgressDialog();
                dialog.Maximum.Value = 100;
                _ = dialog.ShowDialog(GetMainWindow());
                foreach (var (plugin, tasks) in PluginManager.Default._tasks)
                {
                    for (var i = 0; i < tasks.Count; i++)
                    {
                        var task = tasks[i];
                        dialog.Text.Value = string.Format(Strings.IsLoading, plugin.PluginName) + $"  :{task.Name}";

                        await task.RunTask(dialog);
                        dialog.Report(0);
                    }
                }

                dialog.Close();
            }
            app.ServiceProvider = app.Services.BuildServiceProvider();
        }
    }
}