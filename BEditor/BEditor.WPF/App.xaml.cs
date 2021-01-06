using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using System.Xml.Linq;

using BEditor.Models;
using BEditor.ViewModels;
using BEditor.ViewModels.CustomControl;
using BEditor.ViewModels.PropertyControl;
using BEditor.Views;
using BEditor.Views.CustomControl;
using BEditor.Views.MessageContent;

using BEditor.Core.Data;
using BEditor.Core.Data.Property;
using BEditor.Core.Extensions.ViewCommand;

using MaterialDesignThemes.Wpf;
using Resources_ = BEditor.Core.Properties.Resources;
using System.Timers;
using BEditor.Models.Services;
using BEditor.Core.Service;
using BEditor.Core.Command;
using BEditor.Core.Data.Primitive.Properties;
using BEditor.Drawing;
using System.Globalization;
using System.Linq;
using BEditor.WPF.Controls;
using System.Windows.Controls.Primitives;
using System.Reflection;
using System.Threading.Tasks;
using BEditor.ViewModels.MessageContent;
using BEditor.Core.Plugin;

namespace BEditor
{
    /// <summary>
    /// App.xaml の相互作用ロジック
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            SetDarkMode();

            var viewmodel = new SplashWindowViewModel();
            var splashscreen = new SplashWindow()
            {
                DataContext = viewmodel
            };
            MainWindow = splashscreen;
            splashscreen.Show();

            Task.Run(() =>
            {
                const string LoadingColors = "カラーパレットを読み込み中";
                const string LoadingFont = "フォントを読み込み中";
                const string LoadingPlugins = "プラグインを読み込み中";

                viewmodel.Status.Value = LoadingColors;
                InitialColors();
                viewmodel.Status.Value = LoadingFont;
                InitialFontManager();
                viewmodel.Status.Value = LoadingPlugins;
                InitialPlugins();

                this.Dispatcher.Invoke(() =>
                {
                    var mainWindow = new MainWindow();
                    MainWindow = mainWindow;
                    mainWindow.Show();
                    splashscreen.Close();
                });
            });
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

        private static void InitialColors()
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
                            var color = (Color)info.GetValue(null);
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
            var files = Directory.GetFiles(AppData.Current.Path + "\\user\\colors", "*.xml", SearchOption.AllDirectories);

            foreach (var file in files)
            {

                // ファイルの読み込み
                XDocument xml = XDocument.Load(file);


                XElement xElement = xml.Root;
                IEnumerable<XElement> cols = xElement.Elements("Color");

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
        private static void InitialFontManager()
        {
            FontProperty.FontList.AddRange(
                    Settings.Default.IncludeFontDir
                        .Select(dir => Directory.GetFiles(dir))
                        .SelectMany(files => files)
                        .Where(file => Path.GetExtension(file) is ".ttf" or ".ttc" or ".otf")
                        .Select(file => new Font(file)));
        }
        private static void InitialPlugins()
        {
            // すべて
            var all = PluginManager.GetNames();
            // 無効なプラグイン
            var disable = all.Except(Settings.Default.EnablePlugins)
                .Except(Settings.Default.DisablePlugins)
                .ToArray();

            // ここで確認ダイアログを表示
            if (disable.Length != 0)
            {
                var control = new PluginCheckHost();
                var controlvm = new PluginCheckHostViewModel
                {
                    Plugins = new(disable.Select(name => new PluginCheckViewModel() { Name = { Value = name } }))
                };

                control.DataContext = controlvm;

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
            }

            AppData.Current.LoadedPlugins = PluginManager.Load(Settings.Default.EnablePlugins).ToList();
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
        }
    }
}
