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

using Microsoft.WindowsAPICodePack.Dialogs;
using Image = BEditor.Core.Media.Image;
using Resources_ = BEditor.Core.Properties.Resources;
using BEditor.Core.Media;
using System.Timers;
using BEditor.Models.Services;
using BEditor.Core.Service;
using BEditor.Core.Command;
using BEditor.Core.Data.Primitive.Properties;

namespace BEditor
{
    /// <summary>
    /// App.xaml の相互作用ロジック
    /// </summary>
    public partial class App : Application
    {
        private void Application_Startup(object sender, StartupEventArgs e)
        {
            AppData.Current.Arguments = e.Args;


            #region ダークモード設定

            if (Settings.Default.UseDarkMode)
            {
                PaletteHelper paletteHelper = new PaletteHelper();
                ITheme theme = paletteHelper.GetTheme();

                theme.SetBaseTheme(Theme.Dark);

                paletteHelper.SetTheme(theme);
            }

            #endregion

            static void SetFont()
            {
                var ifc = new System.Drawing.Text.InstalledFontCollection();
                //インストールされているすべてのフォントファミリアを取得
                var ffs = ifc.Families;

                foreach (var F in ffs)
                {
                    FontProperty.FontList.Add(new FontRecord() { Name = F.Name });
                }
            }

            static void SetColor()
            {
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

            SetFont();
            SetColor();

            Services.FileDialogService = new FileDialogService();
            Services.ImageRenderService = new WPFImageRenderSevice();

            Message.DialogFunc += (text, iconKind, types) =>
            {
                var control = new MessageUI(types, text, iconKind);
                var dialog = new NoneDialog(control);

                dialog.ShowDialog();

                return control.DialogResult;
            };
            Message.SnackberFunc += (text) => MainWindowViewModel.Current.MessageQueue.Enqueue(text);
        }

        public static (CustomTreeView, VirtualizingStackPanel) CreateTreeObject(ObjectElement obj)
        {
            CustomTreeView _expander = new CustomTreeView()
            {
                HeaderHeight = 35F
            };

            VirtualizingStackPanel stack = new VirtualizingStackPanel() { Margin = new Thickness(32.5, 0, 0, 0) };
            VirtualizingPanel.SetIsVirtualizing(stack, true);
            VirtualizingPanel.SetVirtualizationMode(stack, VirtualizationMode.Recycling);


            VirtualizingStackPanel header = new VirtualizingStackPanel() { Orientation = Orientation.Horizontal };
            VirtualizingPanel.SetIsVirtualizing(header, true);
            VirtualizingPanel.SetVirtualizationMode(header, VirtualizationMode.Recycling);

            _expander.Header = header;

            System.Windows.Controls.CheckBox checkBox = new System.Windows.Controls.CheckBox() { Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center };
            var textBlock = new TextBlock() { Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };

            header.Children.Add(checkBox);
            header.Children.Add(textBlock);


            #region コンテキストメニュー
            ContextMenu menuListBox = new ContextMenu();
            MenuItem Delete = new MenuItem();

            //削除項目の設定
            var menu = new VirtualizingStackPanel() { Orientation = Orientation.Horizontal };
            menu.Children.Add(new PackIcon() { Kind = PackIconKind.Delete, Margin = new Thickness(5, 0, 5, 0) });
            menu.Children.Add(new TextBlock() { Text = "削除", Margin = new Thickness(20, 0, 5, 0) });
            Delete.Header = menu;

            menuListBox.Items.Add(Delete);

            // 作成したコンテキストメニューをListBox1に設定
            _expander.ContextMenu = menuListBox;
            #endregion

            #region イベント
            checkBox.Click += (sender, e) =>
            {
                CommandManager.Do(new EffectElement.CheckCommand(obj, (bool)((System.Windows.Controls.CheckBox)sender).IsChecked));
            };

            #endregion

            #region Binding

            Binding isenablebinding = new Binding("IsEnabled") { Mode = BindingMode.OneWay, Source = obj };
            checkBox.SetBinding(System.Windows.Controls.CheckBox.IsCheckedProperty, isenablebinding);

            Binding textbinding = new Binding("Name") { Mode = BindingMode.OneTime, Source = obj };
            textBlock.SetBinding(TextBlock.TextProperty, textbinding);

            Binding isExpandedbinding = new Binding("IsExpanded") { Mode = BindingMode.TwoWay, Source = obj };
            _expander.SetBinding(CustomTreeView.IsExpandedProperty, isExpandedbinding);

            _expander.SetResourceReference(CustomTreeView.HeaderColorProperty, "MaterialDesignBody");

            #endregion

            _expander.Content = stack;

            return (_expander, stack);
        }
        public static (CustomTreeView, VirtualizingStackPanel) CreateTreeEffect(EffectElement effect)
        {
            var data = effect.Parent;

            CustomTreeView _expander = new CustomTreeView() { HeaderHeight = 35F };

            VirtualizingStackPanel stack = new VirtualizingStackPanel() { Margin = new Thickness(32, 0, 0, 0) };
            VirtualizingPanel.SetIsVirtualizing(stack, true);
            VirtualizingPanel.SetVirtualizationMode(stack, VirtualizationMode.Recycling);

            #region Header

            VirtualizingStackPanel header = new VirtualizingStackPanel() { Orientation = Orientation.Horizontal };
            VirtualizingPanel.SetIsVirtualizing(header, true);
            VirtualizingPanel.SetVirtualizationMode(header, VirtualizationMode.Recycling);

            _expander.Header = header;

            System.Windows.Controls.CheckBox checkBox = new System.Windows.Controls.CheckBox() { Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center };
            Button upbutton = new Button() { Content = new PackIcon() { Kind = PackIconKind.ChevronUp }, Margin = new Thickness(5, 0, 0, 0), Background = null, BorderBrush = null, VerticalAlignment = VerticalAlignment.Center };
            Button downbutton = new Button() { Content = new PackIcon() { Kind = PackIconKind.ChevronDown }, Margin = new Thickness(0, 0, 5, 0), Background = null, BorderBrush = null, VerticalAlignment = VerticalAlignment.Center };
            TextBlock textBlock = new TextBlock() { Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };

            header.Children.Add(checkBox);
            header.Children.Add(upbutton);
            header.Children.Add(downbutton);
            header.Children.Add(textBlock);

            #endregion


            #region コンテキストメニュー
            ContextMenu menuListBox = new ContextMenu();
            MenuItem Delete = new MenuItem();

            //削除項目の設定
            var menu = new VirtualizingStackPanel() { Orientation = Orientation.Horizontal };
            menu.Children.Add(new PackIcon() { Kind = PackIconKind.Delete, Margin = new Thickness(5, 0, 5, 0) });
            menu.Children.Add(new TextBlock() { Text = "削除", Margin = new Thickness(20, 0, 5, 0) });
            Delete.Header = menu;

            menuListBox.Items.Add(Delete);

            // 作成したコンテキストメニューをListBox1に設定
            _expander.ContextMenu = menuListBox;
            #endregion

            #region イベント

            checkBox.Click += (sender, e) => CommandManager.Do(new EffectElement.CheckCommand(effect, (bool)((System.Windows.Controls.CheckBox)sender).IsChecked));

            upbutton.Click += (sender, e) => CommandManager.Do(new EffectElement.UpCommand(effect));

            downbutton.Click += (sender, e) => CommandManager.Do(new EffectElement.DownCommand(effect));

            Delete.Click += (sender, e) => CommandManager.Do(new EffectElement.RemoveCommand(effect));

            #endregion

            #region Binding

            Binding isenablebinding = new Binding("IsEnabled") { Mode = BindingMode.OneWay, Source = effect };
            checkBox.SetBinding(System.Windows.Controls.CheckBox.IsCheckedProperty, isenablebinding);

            Binding textbinding = new Binding("Name") { Mode = BindingMode.OneTime, Source = effect };
            textBlock.SetBinding(TextBlock.TextProperty, textbinding);

            Binding isExpandedbinding = new Binding("IsExpanded") { Mode = BindingMode.TwoWay, Source = effect };
            _expander.SetBinding(CustomTreeView.IsExpandedProperty, isExpandedbinding);

            _expander.SetResourceReference(CustomTreeView.HeaderColorProperty, "MaterialDesignBody");

            #endregion

            _expander.Content = stack;

            return (_expander, stack);
        }

        private void Application_Exit(object sender, ExitEventArgs e)
        {
            Settings.Default.Save();
        }

        private void Application_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            var text = $"[{Resources_.ExceptionMesssage}]\n" +
                $"{e.Exception.Message}\n" +
                $"\n" +
                $"[{Resources_.ExceptionStackTrace}]\n" +
                $"{e.Exception.StackTrace}";

            var path = $"{AppData.Current.Path}\\user\\backup\\{DateTime.Now:yyyy_MM_dd__HH_mm_ss}.bedit";

            TaskDialog dialog = new TaskDialog()
            {
                Caption = Resources_.ErrorHasOccurred,
                Text = Resources_.ErrorMessage + "\n" + string.Format(Resources_.ExceptionOpenFileSaved, path),
                InstructionText = Resources_.ErrorHasOccurred,
                DetailsCollapsedLabel = Resources_.ExceptionInfomationShow,
                DetailsExpandedLabel = Resources_.ExceptionInfomationHidden,
                DetailsExpandedText = text,
                Icon = TaskDialogStandardIcon.Error
            };

            var contractButton = new TaskDialogButton
            {
                Text = Resources_.AppClose
            };
            contractButton.Click += (sender, e) =>
            {
                dialog.Close();
            };
            dialog.Controls.Add(contractButton);

            var continueButton = new TaskDialogButton
            {
                Text = Resources_.Continue
            };
            continueButton.Click += (sender, e) =>
            {
                dialog.Close();
            };
            dialog.Controls.Add(continueButton);

            AppData.Current.Project.Save(path);


            dialog.Show();
            //e.Handled = true;
        }
    }
}
