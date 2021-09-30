using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;

using BEditor.Data;
using BEditor.Models;
using BEditor.Models.ManagePlugins;
using BEditor.Plugin;
using BEditor.Properties;
using BEditor.ViewModels;
using BEditor.ViewModels.Dialogs;
using BEditor.Views;
using BEditor.Views.CustomTitlebars;
using BEditor.Views.Dialogs;

using FluentAvalonia.UI.Controls;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;

using Button = Avalonia.Controls.Button;
using MenuFlyout = FluentAvalonia.UI.Controls.MenuFlyout;

namespace BEditor
{
    public sealed class MainWindow : FluentWindow
    {
        internal readonly StackPanel _notifications;
        internal readonly StackPanel _stackNotifications;
        internal readonly StackPanel _leftMenuBar;
        internal readonly StackPanel _rightMenuBar;
        internal readonly StackPanel _bottomMenuBar;
        internal readonly Popup _notificationsPopup;
        private static readonly ReactiveCommand<BasePluginMenu> _closeMenuCommand = new();
        private static readonly ReactiveCommand<BasePluginMenu> _openMenuCommand = new();
        private static readonly ReactiveCommand<(RadioMenuFlyoutItem Menu, BasePluginMenu PluginMenu)> _leftMenuCommand = new();
        private static readonly ReactiveCommand<(RadioMenuFlyoutItem Menu, BasePluginMenu PluginMenu)> _rightMenuCommand = new();
        private static readonly ReactiveCommand<(RadioMenuFlyoutItem Menu, BasePluginMenu PluginMenu)> _bottomMenuCommand = new();

        private sealed class LayoutConfig
        {
            [JsonPropertyName("columnDefinitions")]
            public string ColumnDefinitions { get; set; } = "Auto,425,Auto,*,Auto,2*,Auto";

            [JsonPropertyName("rowDefinitions")]
            public string RowDefinitions { get; set; } = "Auto,Auto,*,Auto,*,Auto";
        }

        static MainWindow()
        {
            _closeMenuCommand.Subscribe(menu => AppModel.Current.DisplayedMenus.Remove(menu));

            _openMenuCommand.Subscribe(menu =>
            {
                menu.MainWindow = App.GetMainWindow();
                menu.Execute();
            });

            _leftMenuCommand.Subscribe(i =>
            {
                AppModel.Current.DisplayedMenus.Remove(i.PluginMenu);
                i.PluginMenu.MenuLocation = MenuLocation.Left;
                AppModel.Current.DisplayedMenus.Add(i.PluginMenu);
            });

            _rightMenuCommand.Subscribe(i =>
            {
                AppModel.Current.DisplayedMenus.Remove(i.PluginMenu);
                i.PluginMenu.MenuLocation = MenuLocation.Right;
                AppModel.Current.DisplayedMenus.Add(i.PluginMenu);
            });

            _bottomMenuCommand.Subscribe(i =>
            {
                AppModel.Current.DisplayedMenus.Remove(i.PluginMenu);
                i.PluginMenu.MenuLocation = MenuLocation.Bottom;
                AppModel.Current.DisplayedMenus.Add(i.PluginMenu);
            });
        }

        public MainWindow()
        {
            var vm = MainWindowViewModel.Current;
            AddHandler(KeyDownEvent, Window_KeyDown, RoutingStrategies.Tunnel);
            vm.New.Subscribe(CreateProjectClick);

            InitializeComponent();

            _notifications = this.FindControl<StackPanel>("Notifications");
            _stackNotifications = this.FindControl<StackPanel>("NotificationsPanel");
            _leftMenuBar = this.FindControl<StackPanel>("LeftMenuBar");
            _rightMenuBar = this.FindControl<StackPanel>("RightMenuBar");
            _bottomMenuBar = this.FindControl<StackPanel>("BottomMenuBar");
            _notificationsPopup = this.FindControl<Popup>("NotificationsPopup");
            ApplyConfig();
#if DEBUG
            this.AttachDevTools();
#endif
        }

        private void Window_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Source != this) return;

            for (var i = 0; i < KeyBindingModel.Bindings.Count; i++)
            {
                var kb = KeyBindingModel.Bindings[i];
                if (kb.ToKeyGesture().Matches(e))
                {
                    kb.Command?.Command.Execute(null);
                }
            }
        }

        public void ShowNotifications(object? s, RoutedEventArgs e)
        {
            _notificationsPopup.Open();
        }

        public void ObjectsPopupOpen(object s, RoutedEventArgs e)
        {
            this.FindControl<Popup>("ObjectsPopup").Open();
        }

        public void ObjectStartDrag(object s, PointerPressedEventArgs e)
        {
            this.FindControl<Popup>("ObjectsPopup").Close();
            if (s is Control ctr && ctr.DataContext is ObjectMetadata metadata)
            {
                var data = new DataObject();
                data.Set("ObjectMetadata", metadata);
                DragDrop.DoDragDrop(e, data, DragDropEffects.Copy);
            }
        }

        public async void CreateProjectClick(object s)
        {
            if (VisualRoot is Window window)
            {
                var viewmodel = new CreateProjectViewModel();
                var dialog = new CreateProject { DataContext = viewmodel };

                await dialog.ShowDialog(window);
            }
        }

        protected override async void OnOpened(EventArgs e)
        {
            base.OnOpened(e);

            await App.StartupTask;
            App.StartupTask = default;
            await CheckPluginUpdateAsync();

            this.FindControl<Library>("Library").InitializeTreeView();

            this.FindControl<WindowsTitlebar>("Titlebar").InitializePluginMenu();

            AppModel.Current.DisplayedMenus.CollectionChanged += DisplayedMenus_CollectionChanged;
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);

            if (Content is Grid grid)
            {
                var path = Path.Combine(WindowConfig.GetFolder(), "MainWindowLayout.json");
                try
                {
                    var json = JsonSerializer.Serialize(new LayoutConfig
                    {
                        ColumnDefinitions = grid.ColumnDefinitions.ToString(),
                        RowDefinitions = string.Join(",", grid.RowDefinitions.Select(x => x.Height)),
                    }, Packaging.PackageFile._serializerOptions);

                    File.WriteAllText(path, json);
                }
                catch
                {
                }
            }
        }

        private void DisplayedMenus_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            static MenuFlyout CreateMenu(BasePluginMenu menu)
            {
                var close = new MenuFlyoutItem
                {
                    Text = Strings.Close,
                    CommandParameter = menu,
                    Command = _closeMenuCommand,
                };
                var show = new MenuFlyoutItem
                {
                    Text = Strings.Show,
                    CommandParameter = menu,
                    Command = _openMenuCommand,
                };
                var left = new RadioMenuFlyoutItem
                {
                    IsChecked = menu.MenuLocation == MenuLocation.Left,
                    Text = "Left",
                    Command = _leftMenuCommand,
                    GroupName = "Group1",
                };
                var right = new RadioMenuFlyoutItem
                {
                    IsChecked = menu.MenuLocation == MenuLocation.Right,
                    Text = "Right",
                    Command = _rightMenuCommand,
                    GroupName = "Group1",
                };
                var bottom = new RadioMenuFlyoutItem
                {
                    IsChecked = menu.MenuLocation == MenuLocation.Bottom,
                    Text = "Bottom",
                    Command = _bottomMenuCommand,
                    GroupName = "Group1",
                };

                left.CommandParameter = (left, menu);
                right.CommandParameter = (right, menu);
                bottom.CommandParameter = (bottom, menu);

                return new MenuFlyout
                {
                    Items = new MenuFlyoutItemBase[]
                    {
                        show,
                        close,
                        new MenuFlyoutSeparator(),
                        left,
                        right,
                        bottom,
                    },
                };
            }

            var items = AppModel.Current.DisplayedMenus;
            if (e.Action == NotifyCollectionChangedAction.Add)
            {
                var item = items[e.NewStartingIndex];
                var panel = item.MenuLocation switch
                {
                    MenuLocation.Default => null,
                    MenuLocation.Left => _leftMenuBar,
                    MenuLocation.Right => _rightMenuBar,
                    MenuLocation.Bottom => _bottomMenuBar,
                    _ => null,
                };

                if (panel == null) return;

                var layout = new LayoutTransformControl
                {
                    DataContext = item,
                };
                var button = new Button
                {
                    Content = item.Name,
                    DataContext = item,
                    ContextFlyout = CreateMenu(item),
                };

                button.Click += (s, e) =>
                {
                    if (s is Button button && button.DataContext is BasePluginMenu pluginMenu)
                    {
                        pluginMenu.MainWindow = button.GetVisualRoot();
                        pluginMenu.Execute();
                    }
                };

                layout.Child = button;
                panel.Children.Add(layout);
            }
            else if (e.Action == NotifyCollectionChangedAction.Remove)
            {
                var item = (BasePluginMenu)e.OldItems![e.OldStartingIndex]!;
                var panel = item.MenuLocation switch
                {
                    MenuLocation.Default => null,
                    MenuLocation.Left => _leftMenuBar,
                    MenuLocation.Right => _rightMenuBar,
                    MenuLocation.Bottom => _bottomMenuBar,
                    _ => null,
                };

                if (panel?.Children?.FirstOrDefault(i => i.DataContext == item) is IControl ctrl)
                {
                    panel.Children.Remove(ctrl);
                }
            }
            else if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                _leftMenuBar.Children.Clear();
                _rightMenuBar.Children.Clear();
                _bottomMenuBar.Children.Clear();
            }
        }

        private void ApplyConfig()
        {
            if (Content is Grid grid)
            {
                var path = Path.Combine(WindowConfig.GetFolder(), "MainWindowLayout.json");
                if (!File.Exists(path)) return;
                try
                {
                    var json = File.ReadAllText(path);

                    var obj = JsonSerializer.Deserialize<LayoutConfig>(json, Packaging.PackageFile._serializerOptions);
                    if (obj is null) return;

                    var columnDef = new ColumnDefinitions(obj.ColumnDefinitions);
                    var rowDef = new RowDefinitions(obj.RowDefinitions);
                    if (grid.ColumnDefinitions.Count == columnDef.Count)
                    {
                        grid.ColumnDefinitions = new(obj.ColumnDefinitions);
                    }

                    if (grid.RowDefinitions.Count == rowDef.Count)
                    {
                        grid.RowDefinitions = new(obj.RowDefinitions);
                    }
                }
                catch
                {
                }
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private static async Task CheckPluginUpdateAsync()
        {
            var service = AppModel.Current.ServiceProvider.GetRequiredService<PluginUpdateService>();
            var message = AppModel.Current.ServiceProvider.GetRequiredService<IMessage>();
            await service.CheckUpdateAsync();

            await Task.Yield();

            foreach (var item in service.Updates)
            {
                message.Snackbar(
                    string.Format(Strings.ThereIsANewerVersionOf, item.Plugin.PluginName),
                    string.Empty,
                    IMessage.IconType.None,
                    action: _ => AppModel.Current.Navigate(new Uri("beditor://manage-plugin/update")),
                    actionName: Strings.Update);
            }
        }
    }
}