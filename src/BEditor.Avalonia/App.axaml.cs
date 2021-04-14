using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Templates;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;

using BEditor.Data;
using BEditor.Models;
using BEditor.Plugin;
using BEditor.Primitive;
using BEditor.Properties;

using FFMediaToolkit;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BEditor
{
    public class App : Application
    {
        public static readonly string FFmpegDir = Path.Combine(AppContext.BaseDirectory, "ffmpeg");
        public static readonly ILogger? Logger = AppModel.Current.LoggingFactory.CreateLogger<App>();
        public static readonly DispatcherTimer BackupTimer = new()
        {
            Interval = TimeSpan.FromMinutes(Settings.Default.BackUpInterval)
        };
        private static readonly string colorsDir = Path.Combine(AppContext.BaseDirectory, "user", "colors");
        private static readonly string backupDir = Path.Combine(AppContext.BaseDirectory, "user", "backup");
        private static readonly string pluginsDir = Path.Combine(AppContext.BaseDirectory, "user", "plugins");

        public static Window GetMainWindow()
        {
            if (Current.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                return desktop.MainWindow;
            }

            throw new Exception();
        }

        public override void Initialize()
        {
            CultureInfo.CurrentCulture = new(Settings.Default.Language);
            CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture;

            AvaloniaXamlLoader.Load(this);

            Styles.Insert(0, new FluentTheme(new Uri("avares://BEditor.Avalonia/App.axaml"))
            {
                Mode = Settings.Default.UseDarkMode ? FluentThemeMode.Dark : FluentThemeMode.Light
            });
        }

        public override async void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                RegisterPrimitive();

                desktop.MainWindow = new MainWindow();

                CreateDirectory();

                await InitialPluginsAsync();

                AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

                RunBackup();
            }

            base.OnFrameworkInitializationCompleted();
        }

        private async void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            await using var provider = AppModel.Current.Services.BuildServiceProvider();
            provider.GetService<IMessage>()!
                .Snackbar(string.Format(Strings.ExceptionWasThrown, e.ExceptionObject.ToString()));

            Logger?.LogError(e.ExceptionObject as Exception, "UnhandledException was thrown.");

            //#if !DEBUG
            //            e.Handled = true;
            //#endif
        }
        private static void CreateDirectory()
        {
            DirectoryManager.Default.Directories.Add(colorsDir);
            DirectoryManager.Default.Directories.Add(backupDir);
            DirectoryManager.Default.Directories.Add(pluginsDir);
            DirectoryManager.Default.Directories.Add(FFmpegDir);

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

                        var files = Directory.GetFiles(dir).Select(i => new FileInfo(i)).OrderByDescending(i => i.LastWriteTime).ToArray();
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

            // すべて
            var all = PluginManager.Default.GetNames();
            // 無効なプラグイン
            var disable = all.Except(Settings.Default.EnablePlugins)
                .Except(Settings.Default.DisablePlugins)
                .ToArray();

            // ここで確認ダイアログを表示
            if (disable.Length != 0)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    //var controlvm = new PluginCheckHostViewModel
                    //{
                    //    Plugins = new(disable.Select(name => new PluginCheckViewModel() { Name = { Value = name } }))
                    //};
                    //var control = new PluginCheckHost(controlvm);

                    //new NoneDialog(control).ShowDialog();

                    //foreach (var vm in controlvm.Plugins)
                    //{
                    //    if (vm.IsEnabled.Value)
                    //    {
                    //        Settings.Default.EnablePlugins.Add(vm.Name.Value);
                    //    }
                    //    else
                    //    {
                    //        Settings.Default.DisablePlugins.Add(vm.Name.Value);
                    //    }
                    //}

                    //Settings.Default.Save();
                });
            }

            PluginManager.Default.Load(Settings.Default.EnablePlugins);

            AppModel.Current.ServiceProvider = AppModel.Current.Services.BuildServiceProvider();
        }
    }
}
