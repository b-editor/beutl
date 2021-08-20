using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

using BEditor.Data;
using BEditor.Models;
using BEditor.Properties;
using BEditor.ViewModels;
using BEditor.ViewModels.DialogContent;
using BEditor.ViewModels.Dialogs;
using BEditor.Views;
using BEditor.Views.CustomTitlebars;
using BEditor.Views.DialogContent;
using BEditor.Views.Dialogs;

using OpenTK.Audio.OpenAL;

namespace BEditor
{
    public sealed class MainWindow : FluentWindow
    {
        internal readonly FluentAvalonia.UI.Controls.Primitives.InfoBarPanel _notifications;
        internal readonly FluentAvalonia.UI.Controls.Primitives.InfoBarPanel _stackNotifications;
        internal readonly Popup _notificationsPopup;

        private sealed class LayoutConfig
        {
            [JsonPropertyName("columnDefinitions")]
            public string ColumnDefinitions { get; set; } = "425,Auto,*,Auto,2*";

            [JsonPropertyName("rowDefinitions")]
            public string RowDefinitions { get; set; } = "Auto,Auto,*,Auto,*,Auto";
        }

        public MainWindow()
        {
            var vm = MainWindowViewModel.Current;
            AddHandler(KeyDownEvent, Window_KeyDown, RoutingStrategies.Tunnel);
            vm.New.Subscribe(CreateProjectClick);

            InitializeComponent();

            _notifications = this.FindControl<FluentAvalonia.UI.Controls.Primitives.InfoBarPanel>("Notifications");
            _stackNotifications = this.FindControl<FluentAvalonia.UI.Controls.Primitives.InfoBarPanel>("NotificationsPanel");
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

            this.FindControl<Library>("Library").InitializeTreeView();

            this.FindControl<WindowsTitlebar>("Titlebar").InitializePluginMenu();
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

                    grid.ColumnDefinitions = new(obj.ColumnDefinitions);
                    grid.RowDefinitions = new(obj.RowDefinitions);
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
    }
}