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
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using System.Xml.Linq;

using BEditor.Data;
using BEditor.Drawing;
using BEditor.Models;
using BEditor.Plugin;
using BEditor.Primitive;
using BEditor.Primitive.Effects;
using BEditor.Primitive.Objects;
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

using Resource = BEditor.Properties.Resources;

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

        protected override void OnStartup(StartupEventArgs e)
        {
            CultureInfo.CurrentCulture = new(Settings.Default.Language);
            CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture;
            CreateDirectory();
            base.OnStartup(e);

            SetDarkMode();
            ProjectModel.Current.CreateEvent += (_, _) =>
            {
                var d = new NoneDialog()
                {
                    Content = new ProjectCreatePage(),
                    Owner = MainWindow,
                    MaxWidth = double.PositiveInfinity,
                };
                d.ShowDialog();
            };

            var viewmodel = new SplashWindowViewModel();
            var splashscreen = new SplashWindow()
            {
                DataContext = viewmodel
            };
            MainWindow = splashscreen;
            splashscreen.Show();

            Task.Run(async () =>
            {
                RegisterPrimitive();

                viewmodel.Status.Value = string.Format(MessageResources.IsLoading, Resource.ColorPalette);
                await InitialColorsAsync();

                viewmodel.Status.Value = string.Format(MessageResources.IsLoading, Resource.Font);
                await Task.Run(() => FontManager.Default);

                viewmodel.Status.Value = string.Format(MessageResources.IsLoading, Resource.Plugins);
                await InitialPlugins();

                viewmodel.Status.Value = string.Format(MessageResources.IsChecking, Resource.Library);

                var msg = AppData.Current.Message;

                await InitFFmpeg();
                if (!CheckOpenAL())
                {
                    if (msg!.Dialog(MessageResources.OpenALNotFound, IMessage.IconType.Info, new IMessage.ButtonType[] { IMessage.ButtonType.Yes, IMessage.ButtonType.No }) is IMessage.ButtonType.Yes)
                    {
                        Process.Start(new ProcessStartInfo("cmd", $"/c start https://www.openal.org/downloads/") { CreateNoWindow = true });
                    }
                    else
                    {
                        Shutdown();
                    }
                }


                await Dispatcher.Invoke(async () =>
                {
                    var file = e.Args.FirstOrDefault() is string str
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

                    splashscreen.Close();
                });

                Settings.Default.Save();
            });

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
        private async Task InitFFmpeg()
        {
            var installer = new FFmpegInstaller(ffmpegDir);
            var msg = AppData.Current.Message;

            if (!await installer.IsInstalledAsync())
            {
                if (msg!.Dialog(MessageResources.FFmpegNotFound, IMessage.IconType.Info, new IMessage.ButtonType[] { IMessage.ButtonType.Yes, IMessage.ButtonType.No }) is IMessage.ButtonType.Yes)
                {
                    try
                    {
                        Loading loading = null!;
                        NoneDialog dialog = null!;

                        void start(object? s, EventArgs e)
                        {
                            Dispatcher.Invoke(() =>
                            {
                                loading = new Loading()
                                {
                                    Maximum = { Value = 100 },
                                    Minimum = { Value = 0 }
                                };
                                dialog = new NoneDialog(loading);

                                dialog.Show();

                                loading.Text.Value = string.Format(MessageResources.IsDownloading, "FFmpeg");
                            });
                        }
                        void downloadComp(object? s, AsyncCompletedEventArgs e)
                        {
                            loading.Text.Value = string.Format(MessageResources.IsExtractedAndPlaced, "FFmpeg");
                            loading.IsIndeterminate.Value = true;
                        }
                        void progress(object s, DownloadProgressChangedEventArgs e)
                        {
                            loading.NowValue.Value = e.ProgressPercentage;
                        }
                        void installed(object? s, EventArgs e) => Dispatcher.InvokeAsync(dialog.Close);

                        installer.StartInstall += start;
                        installer.Installed += installed;
                        installer.DownloadCompleted += downloadComp;
                        installer.DownloadProgressChanged += progress;

                        await installer.Install();

                        installer.StartInstall -= start;
                        installer.Installed -= installed;
                        installer.DownloadCompleted -= downloadComp;
                        installer.DownloadProgressChanged -= progress;
                    }
                    catch (Exception e)
                    {
                        msg.Dialog(string.Format(MessageResources.FailedToInstall, "FFmpeg"));

                        Logger.LogError(e, "Failed to install ffmpeg.");
                    }
                }
                else
                {
                    msg.Dialog(MessageResources.SomeFunctionsAreNotAvailable_);
                }
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
                    backupTimer.Interval = TimeSpan.FromMinutes(Settings.Default.BackUpInterval);
                }
            };

            backupTimer.Start();

            //Task.Run(async () =>
            //{
            //    while (true)
            //    {
            //        await Task.Delay(TimeSpan.FromMinutes(Settings.Default.BackUpInterval));

            //        var proj = AppData.Current.Project;
            //        if (proj is not null && Settings.Default.AutoBackUp)
            //        {
            //            var dir = Path.Combine(proj.DirectoryName, "backup");
            //            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            //            proj.Save(Path.Combine(dir, DateTime.Now.ToString("HH:mm:ss").Replace(':', '_')) + ".backup");

            //            var files = Directory.GetFiles(dir).Select(i => new FileInfo(i)).OrderBy(i => i.LastWriteTime).ToArray();
            //            if (files.Length is > 10)
            //            {
            //                foreach (var file in files.Skip(10))
            //                {
            //                    if (file.Exists) file.Delete();
            //                }
            //            }
            //        }
            //    }
            //});
        }
        private static void RegisterPrimitive()
        {
            ObjectMetadata.LoadedObjects.Add(PrimitiveTypes.VideoMetadata);
            ObjectMetadata.LoadedObjects.Add(PrimitiveTypes.ImageMetadata);
            ObjectMetadata.LoadedObjects.Add(PrimitiveTypes.FigureMetadata);
            ObjectMetadata.LoadedObjects.Add(PrimitiveTypes.PolygonMetadata);
            ObjectMetadata.LoadedObjects.Add(PrimitiveTypes.RoundRectMetadata);
            ObjectMetadata.LoadedObjects.Add(PrimitiveTypes.TextMetadata);
            ObjectMetadata.LoadedObjects.Add(PrimitiveTypes.CameraMetadata);
            ObjectMetadata.LoadedObjects.Add(PrimitiveTypes.GL3DObjectMetadata);
            ObjectMetadata.LoadedObjects.Add(PrimitiveTypes.SceneMetadata);
            ObjectMetadata.LoadedObjects.Add(PrimitiveTypes.FramebufferMetadata);
            ObjectMetadata.LoadedObjects.Add(PrimitiveTypes.ListenerMetadata);

            EffectMetadata.LoadedEffects.Add(new(Resource.Effects)
            {
                Children = new EffectMetadata[]
                {
                    EffectMetadata.Create<Border>(Resource.Border),
                    EffectMetadata.Create<StrokeText>($"{Resource.Border} ({Resource.Text})"),
                    EffectMetadata.Create<ColorKey>(Resource.ColorKey),
                    EffectMetadata.Create<Shadow>(Resource.DropShadow),
                    EffectMetadata.Create<Blur>(Resource.Blur),
                    EffectMetadata.Create<Monoc>(Resource.Monoc),
                    EffectMetadata.Create<Dilate>(Resource.Dilate),
                    EffectMetadata.Create<Erode>(Resource.Erode),
                    EffectMetadata.Create<Clipping>(Resource.Clipping),
                    EffectMetadata.Create<AreaExpansion>(Resource.AreaExpansion),
                    EffectMetadata.Create<LinearGradient>(Resource.LinearGradient),
                    EffectMetadata.Create<CircularGradient>(Resource.CircularGradient),
                    EffectMetadata.Create<Mask>(Resource.Mask),
                    EffectMetadata.Create<PointLightDiffuse>(Resource.PointLightDiffuse),
                    EffectMetadata.Create<ChromaKey>(Resource.ChromaKey),
                    EffectMetadata.Create<ImageSplit>(Resource.ImageSplit),
                    EffectMetadata.Create<MultipleControls>(Resource.MultipleImageControls),
                }
            });
            EffectMetadata.LoadedEffects.Add(new(Resource.Camera)
            {
                Children = new EffectMetadata[]
                {
                    EffectMetadata.Create<DepthTest>(Resource.DepthTest),
                    EffectMetadata.Create<PointLightSource>(Resource.PointLightSource),
                }
            });
#if DEBUG
            EffectMetadata.LoadedEffects.Add(new("TestEffect", () => new TestEffect()));
#endif
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
            PluginBuilder.config = new PluginConfig(AppData.Current);

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

        private async void Application_Exit(object sender, ExitEventArgs e)
        {
            Settings.Default.Save();

            // 最近使ったフォントの保存
            {
                var jsonFile = Path.Combine(AppContext.BaseDirectory, "user", "usedFonts.json");
                await using var stream = new FileStream(jsonFile, FileMode.Create);

                await JsonSerializer.SerializeAsync(stream, FontDialogViewModel.UsedFonts.Select(i => i.Font.Filename), new JsonSerializerOptions()
                {
                    WriteIndented = true
                });
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
                .Snackbar(string.Format(Resource.ExceptionWasThrown, e.Exception.GetType().FullName));

            Logger?.LogError(e.Exception, "UnhandledException was thrown.");

#if !DEBUG
            e.Handled = true;
#endif
        }
    }
}
