using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using System.Xml.Linq;

using BEditor.Data;
using BEditor.Drawing;
using BEditor.Media;
using BEditor.Models;
using BEditor.Plugin;
using BEditor.Primitive;
using BEditor.Properties;
using BEditor.ViewModels;
using BEditor.ViewModels.CustomControl;
using BEditor.ViewModels.MessageContent;
using BEditor.ViewModels.PropertyControl;
using BEditor.Views;
using BEditor.Views.CreatePage;
using BEditor.Views.MessageContent;

using MaterialDesignThemes.Wpf;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using OpenTK.Audio.OpenAL;

namespace BEditor
{
    /// <summary>
    /// App.xaml の相互作用ロジック
    /// </summary>
    public partial class App : Application
    {
        private static readonly string colorsDir = Path.Combine(AppContext.BaseDirectory, "user", "colors");
        private static readonly string backupDir = Path.Combine(AppContext.BaseDirectory, "user", "backup");
        private static readonly string pluginsDir = Path.Combine(AppContext.BaseDirectory, "user", "plugins");
        private static readonly string ffmpegDir = Path.Combine(AppContext.BaseDirectory, "ffmpeg");
        public static readonly ILogger? Logger = AppData.Current.LoggingFactory.CreateLogger<App>();
        private static DispatcherTimer? backupTimer;

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            CultureInfo.CurrentCulture = new(Settings.Default.Language);
            CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture;
            CreateDirectory();
            AppData.Current.UIThread = SynchronizationContext.Current;
            SetDarkMode();

            var viewmodel = new SplashWindowViewModel();
            var splashscreen = new SplashWindow()
            {
                DataContext = viewmodel
            };
            MainWindow = splashscreen;
            splashscreen.Show();

            await Task.Run(async () =>
            {
                RegisterPrimitive();

                viewmodel.Status.Value = string.Format(Strings.IsLoading, Strings.ColorPalette);
                await InitialColorsAsync();

                viewmodel.Status.Value = string.Format(Strings.IsLoading, Strings.Font);
                await Task.Run(() => FontManager.Default);

                viewmodel.Status.Value = string.Format(Strings.IsLoading, Strings.Plugins);
                await InitialPlugins();

                viewmodel.Status.Value = string.Format(Strings.IsChecking, Strings.Library);

                var msg = AppData.Current.Message;

                if (!CheckOpenAL())
                {
                    if (await msg!.DialogAsync(Strings.OpenALNotFound, IMessage.IconType.Info, new IMessage.ButtonType[] { IMessage.ButtonType.Yes, IMessage.ButtonType.No }) is IMessage.ButtonType.Yes)
                    {
                        Process.Start(new ProcessStartInfo("cmd", $"/c start https://www.openal.org/downloads/") { CreateNoWindow = true });
                    }
                    else
                    {
                        Shutdown();
                    }
                }

                await Dispatcher.InvokeAsync(async () => await StartupCore());
            });

            splashscreen.Close();
        }

        public async Task StartupCore()
        {
            ProjectModel.Current.CreateEvent += (_, _) =>
            {
                var view = new ProjectCreatePage();

                var d = new NoneDialog()
                {
                    Content = view,
                    Owner = MainWindow,
                    MaxWidth = double.PositiveInfinity,
                };
                d.ShowDialog();

                if (view.DataContext is IDisposable disposable) disposable.Dispose();
            };

            var file = Environment.GetCommandLineArgs().FirstOrDefault() is string str
                && File.Exists(str)
                && Path.GetExtension(str) is ".bedit"
                ? str : null;

            if (file is not null)
            {
                await ProjectModel.DirectOpen(file);

                var win = new MainWindow();
                MainWindow = win;
                win.Show();
            }
            else if (Settings.Default.ShowStartWindow)
            {
                var startWindow = new StartWindow();
                MainWindow = startWindow;
                startWindow.Show();
            }
            else
            {
                var mainWindow = new MainWindow();
                MainWindow = mainWindow;
                mainWindow.Show();
            }

            RunBackup();
        }

        private static void CreateDirectory()
        {
            DirectoryManager.Default.Directories.Add(colorsDir);
            DirectoryManager.Default.Directories.Add(backupDir);
            DirectoryManager.Default.Directories.Add(pluginsDir);
            DirectoryManager.Default.Directories.Add(ffmpegDir);

            DirectoryManager.Default.Run();
        }
        private static void SetDarkMode()
        {
            if (Settings.Default.UseDarkMode)
            {
                var paletteHelper = new PaletteHelper();
                ITheme theme = paletteHelper.GetTheme();

                theme.SetBaseTheme(Theme.Dark);

                paletteHelper.SetTheme(theme);
            }
        }

        private static bool CheckOpenAL()
        {
            try
            {
                AL.Get(ALGetFloat.DopplerFactor);

                return true;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
        }
        private static void RunBackup()
        {
            backupTimer = new DispatcherTimer()
            {
                Interval = TimeSpan.FromMinutes(Settings.Default.BackUpInterval)
            };

            backupTimer.Tick += (s, e) =>
            {
                Task.Run(() =>
                {
                    var proj = AppData.Current.Project;
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
                    backupTimer.Interval = TimeSpan.FromMinutes(Settings.Default.BackUpInterval);
                }
            };

            backupTimer.Start();
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
        private static async Task InitialColorsAsync()
        {
            static void CreateDefaultColor()
            {
                var file = Path.Combine(AppContext.BaseDirectory, "user", "colors", "MaterialDesignColors.xml");

                if (!File.Exists(file))
                {
                    var elements = typeof(Color).GetFields()
                        .Where(info => info.IsStatic)
                        .Select(info =>
                        {
                            var color = (Color)info.GetValue(null)!;
                            var name = info.Name;

                            return new XElement("Color",
                                new XAttribute("Name", name),
                                new XAttribute("Red", color.R),
                                new XAttribute("Green", color.G),
                                new XAttribute("Blue", color.B));
                        });

                    var XDoc = new XDocument(
                        new XDeclaration("1.0", "utf-8", "true"),
                        new XElement("Colors", new XAttribute("Name", "MaterialDesignColors"))
                    );

                    foreach (var element in elements) XDoc.Elements().First().Add(element);

                    XDoc.Save(file);
                }
            }

            CreateDefaultColor();
            var files = Directory.GetFiles(AppContext.BaseDirectory + "\\user\\colors", "*.xml", SearchOption.AllDirectories);

            foreach (var file in files)
            {
                using var stream = new FileStream(file, FileMode.Open);
                // ファイルの読み込み
                var xml = await XDocument.LoadAsync(stream, LoadOptions.None, default);


                var xElement = xml.Root;

                if (xElement is not null)
                {
                    var cols = xElement.Elements("Color");

                    ObservableCollection<ColorListProperty> colors = new();

                    foreach (XElement col in cols)
                    {
                        string name = col.Attribute("Name")?.Value ?? "?";
                        byte red = byte.Parse(col.Attribute("Red")?.Value ?? "0");
                        byte green = byte.Parse(col.Attribute("Green")?.Value ?? "0");
                        byte blue = byte.Parse(col.Attribute("Blue")?.Value ?? "0");

                        colors.Add(new ColorListProperty(red, green, blue, name));
                    }

                    ColorPickerViewModel.ColorList.Add(new ColorList(colors, xElement.Attribute("Name")?.Value ?? "?"));
                }
            }
        }
        private static async ValueTask InitialPlugins()
        {
            PluginBuilder.Config = new PluginConfig(AppData.Current);

            // すべて
            var all = PluginManager.Default.GetNames();
            // 無効なプラグイン
            var disable = all.Except(Settings.Default.EnablePlugins)
                .Except(Settings.Default.DisablePlugins)
                .ToArray();

            // ここで確認ダイアログを表示
            if (disable.Length != 0)
            {
                await Current.Dispatcher.InvokeAsync(() =>
                {
                    var controlvm = new PluginCheckHostViewModel
                    {
                        Plugins = new(disable.Select(name => new PluginCheckViewModel() { Name = { Value = name } }))
                    };
                    var control = new PluginCheckHost(controlvm);

                    new NoneDialog(control).ShowDialog();

                    foreach (var vm in controlvm.Plugins)
                    {
                        if (vm.IsEnabled.Value)
                        {
                            Settings.Default.EnablePlugins.Add(vm.Name.Value);
                        }
                        else
                        {
                            Settings.Default.DisablePlugins.Add(vm.Name.Value);
                        }
                    }

                    Settings.Default.Save();
                });
            }

            PluginManager.Default.Load(Settings.Default.EnablePlugins);

            AppData.Current.ServiceProvider = AppData.Current.Services.BuildServiceProvider();
        }

        private void Application_Exit(object sender, ExitEventArgs e)
        {
            Settings.Default.Save();

            // 最近使ったフォントの保存
            {
                var jsonFile = Path.Combine(AppContext.BaseDirectory, "user", "usedFonts.json");
                var fontfiles = FontDialogViewModel.UsedFonts.Select(i => i.Font.Filename).ToArray();
                using var stream = new FileStream(jsonFile, FileMode.Create);
                using var writer = new StreamWriter(stream);

                writer.Write(JsonSerializer.Serialize(fontfiles, new JsonSerializerOptions()
                {
                    WriteIndented = true
                }));
            }

            backupTimer?.Stop();
            DirectoryManager.Default.Stop();

            var app = AppData.Current;

            app.ServiceProvider.GetService<HttpClient>()?.Dispose();

            app.Project?.Unload();
            app.Project = null;
        }

        private async void Application_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            await using var provider = AppData.Current.Services.BuildServiceProvider();
            provider.GetService<IMessage>()!
                .Snackbar(string.Format(Strings.ExceptionWasThrown, e.Exception.GetType().FullName));

            Logger?.LogError(e.Exception, "UnhandledException was thrown.");

#if !DEBUG
            e.Handled = true;
#endif
        }
    }
}