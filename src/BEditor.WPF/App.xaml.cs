using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using System.Xml.Linq;

using BEditor.Core;
using BEditor.Core.Data;
using BEditor.Core.Data.Property;
using BEditor.Core.Extensions;
using BEditor.Core.Plugin;
using BEditor.Core.Service;
using BEditor.Drawing;
using BEditor.Models;
using BEditor.Models.Services;
using BEditor.Primitive;
using BEditor.Primitive.Effects;
using BEditor.Primitive.Objects;
using BEditor.ViewModels;
using BEditor.ViewModels.CustomControl;
using BEditor.ViewModels.MessageContent;
using BEditor.ViewModels.PropertyControl;
using BEditor.Views;
using BEditor.Views.CreatePage;
using BEditor.Views.MessageContent;

using MaterialDesignThemes.Wpf;

using SkiaSharp;

using DirectoryManager = BEditor.Core.DirectoryManager;
using Resource = BEditor.Core.Properties.Resources;

namespace BEditor
{
    /// <summary>
    /// App.xaml の相互作用ロジック
    /// </summary>
    public partial class App : Application
    {
        private static readonly string colorsDir = Path.Combine(AppContext.BaseDirectory, "user", "colors");
        private static readonly string logsDir = Path.Combine(AppContext.BaseDirectory, "user", "logs");
        private static readonly string backupDir = Path.Combine(AppContext.BaseDirectory, "user", "backup");
        private static readonly string pluginsDir = Path.Combine(AppContext.BaseDirectory, "user", "plugins");
        private static readonly string errorlogFile = Path.Combine(AppContext.BaseDirectory, "user", "logs", "errorlog.xml");

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
#if !DEBUG

            var viewmodel = new SplashWindowViewModel();
            var splashscreen = new SplashWindow()
            {
                DataContext = viewmodel
            };
            MainWindow = splashscreen;
            splashscreen.Show();
#endif

            Task.Run(async () =>
            {
#if !DEBUG

                const string LoadingColors = "カラーパレットを読み込み中";
                const string LoadingFont = "フォントを読み込み中";
                const string LoadingPlugins = "プラグインを読み込み中";
                const string LoadingCommand = "コマンドを読み込み中";

                RegisterPrimitive();

                viewmodel.Status.Value = LoadingColors;
                await InitialColorsAsync();

                viewmodel.Status.Value = LoadingFont;
                InitialFontManager();

                viewmodel.Status.Value = LoadingPlugins;
                InitialPlugins();

                viewmodel.Status.Value = LoadingCommand;
                LoadCommand();

#else
                RegisterPrimitive();

                await InitialColorsAsync();

                InitialFontManager();

                InitialPlugins();

                LoadCommand();
#endif

                Dispatcher.Invoke(() =>
                {
                    var file = e.Args.FirstOrDefault() is string str
                        && File.Exists(str)
                        && Path.GetExtension(str) is ".bedit"
                        ? str : null;

                    if (file is not null)
                    {
                        var app = AppData.Current;
                        app.Project?.Unload();
                        var project = new Project(file);
                        project.Load();
                        app.Project = project;
                        app.AppStatus = Status.Edit;

                        Settings.Default.MostRecentlyUsedList.Remove(file);
                        Settings.Default.MostRecentlyUsedList.Add(file);

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
#if !DEBUG
                    splashscreen.Close();
#endif
                });

                Settings.Default.Save();
            });
        }

        private static void CreateDirectory()
        {
            DirectoryManager.Default.Directories.Add(colorsDir);
            DirectoryManager.Default.Directories.Add(logsDir);
            DirectoryManager.Default.Directories.Add(backupDir);
            DirectoryManager.Default.Directories.Add(pluginsDir);

            DirectoryManager.Default.Run();

            if (!File.Exists(errorlogFile))
            {
                XDocument XDoc = new XDocument(
                    new XDeclaration("1.0", "utf-8", "true"),
                    new XElement("Logs")
                );

                XDoc.Save(errorlogFile);
            }
        }
        private static void SetDarkMode()
        {
            if (Settings.Default.UseDarkMode)
            {
                PaletteHelper paletteHelper = new PaletteHelper();
                ITheme theme = paletteHelper.GetTheme();

                theme.SetBaseTheme(Theme.Dark);

                paletteHelper.SetTheme(theme);
            }
        }

        private static void RegisterPrimitive()
        {
            Serialize.SerializeKnownTypes.AddRange(new Type[]
            {
                typeof(AudioObject),
                typeof(CameraObject),
                typeof(GL3DObject),
                typeof(Figure),
                typeof(ImageFile),
                typeof(Text),
                typeof(VideoFile),
                typeof(SceneObject),

                typeof(Blur),
                typeof(Border),
                typeof(ColorKey),
                typeof(Dilate),
                typeof(Erode),
                typeof(Monoc),
                typeof(Shadow),
                typeof(Clipping),
                typeof(AreaExpansion),
                typeof(LinearGradient),
                typeof(CircularGradient),
                typeof(Mask),
                typeof(PointLightDiffuse),
                typeof(ChromaKey),
                typeof(ImageSplit),
                typeof(MultipleControls),
                typeof(DepthTest),
                typeof(DirectionalLightSource),
                typeof(PointLightSource),
                typeof(SpotLight),
            });

            ObjectMetadata.LoadedObjects.Add(PrimitiveTypes.VideoMetadata);
            ObjectMetadata.LoadedObjects.Add(PrimitiveTypes.ImageMetadata);
            ObjectMetadata.LoadedObjects.Add(PrimitiveTypes.FigureMetadata);
            ObjectMetadata.LoadedObjects.Add(PrimitiveTypes.PolygonMetadata);
            ObjectMetadata.LoadedObjects.Add(PrimitiveTypes.RoundRectMetadata);
            ObjectMetadata.LoadedObjects.Add(PrimitiveTypes.TextMetadata);
            ObjectMetadata.LoadedObjects.Add(PrimitiveTypes.CameraMetadata);
            ObjectMetadata.LoadedObjects.Add(PrimitiveTypes.GL3DObjectMetadata);
            ObjectMetadata.LoadedObjects.Add(PrimitiveTypes.SceneMetadata);

            EffectMetadata.LoadedEffects.Add(new(Resource.Effects)
            {
                Children = new EffectMetadata[]
                {
                    new(Resource.Border, () => new Border()),
                    new(Resource.ColorKey, () => new ColorKey()),
                    new(Resource.DropShadow, () => new Shadow()),
                    new(Resource.Blur, () => new Blur()),
                    new(Resource.Monoc, () => new Monoc()),
                    new(Resource.Dilate, () => new Dilate()),
                    new(Resource.Erode, () => new Erode()),
                    new(Resource.Clipping, () => new Clipping()),
                    new(Resource.AreaExpansion, () => new AreaExpansion()),
                    new(Resource.LinearGradient, () => new LinearGradient()),
                    new(Resource.CircularGradient, () => new CircularGradient()),
                    new(Resource.Mask, () => new Mask()),
                    new(Resource.PointLightDiffuse, () => new PointLightDiffuse()),
                    new(Resource.ChromaKey, () => new ChromaKey()),
                    new(Resource.ImageSplit, () => new ImageSplit()),
                    new(Resource.MultipleImageControls, () => new MultipleControls()),
                }
            });
            EffectMetadata.LoadedEffects.Add(new(Resource.Camera)
            {
                Children = new EffectMetadata[]
                {
                    new(Resource.DepthTest, () => new DepthTest()),
                    new(Resource.DirectionalLightSource, () => new DirectionalLightSource()),
                    new(Resource.PointLightSource, () => new PointLightSource()),
                    new(Resource.SpotLight, () => new SpotLight()),
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

                    XDocument XDoc = new XDocument(
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
        public static void InitialFontManager()
        {
            FontProperty.FontList.AddRange(
                SKFontManager.Default.FontFamilies
                    .Select(name => Font.FromFamilyName(name)!)
                    .Where(f => f is not null)
                    .OrderBy(f => f.FamilyName));
            FontProperty.FontList.AddRange(
                    Settings.Default.IncludeFontDir
                        .Where(dir => Directory.Exists(dir))
                        .Select(dir => Directory.GetFiles(dir))
                        .SelectMany(files => files)
                        .Where(file => Path.GetExtension(file) is ".ttf" or ".ttc" or ".otf")
                        .Select(file => new Font(file))
                        .OrderBy(f => f.FamilyName));
        }
        private static void InitialPlugins()
        {
            // すべて
            var all = PluginManager.Default.GetNames();
            // 無効なプラグイン
            var disable = all.Except(Settings.Default.EnablePlugins)
                .Except(Settings.Default.DisablePlugins)
                .ToArray();

            // ここで確認ダイアログを表示
            if (disable.Length != 0)
            {
                App.Current.Dispatcher.Invoke(() =>
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


                    PluginManager.Default.Load(Settings.Default.EnablePlugins);
                });

                return;
            }

            PluginManager.Default.Load(Settings.Default.EnablePlugins);
        }
        private static void LoadCommand()
        {
            //var types = AppData.Current.LoadedPlugins.Select(p => p.GetType().Assembly)
            //    .Append(typeof(ClipData).Assembly)
            //    .Select(p => p.GetTypes())
            //    .Select(p => p.Select(c => c.GetNestedTypes()))
            //    .SelectMany(p => p)
            //    .SelectMany(p => p)
            //    .Where(p => typeof(IRecordCommand).IsAssignableFrom(p))
            //    .ToList();
        }

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            Services.FileDialogService = new FileDialogService();

            Message.DialogFunc += (text, iconKind, types) =>
            {
                var control = new MessageUI(types, text, iconKind);
                var dialog = new NoneDialog(control);

                dialog.ShowDialog();

                return control.DialogResult;
            };
            Message.SnackberFunc += (text) => MainWindowViewModel.Current.MessageQueue.Enqueue(text);
        }

        private void Application_Exit(object sender, ExitEventArgs e)
        {
            Settings.Default.Save();
            DirectoryManager.Default.Stop();
        }

        private void Application_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Message.Snackbar(string.Format(Core.Properties.Resources.ExceptionWasThrown, e.Exception.GetType().FullName));

#if !DEBUG
            e.Handled = true;
#endif
        }
    }
}
