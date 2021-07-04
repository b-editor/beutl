using System;
using System.Linq;
using System.Reactive.Linq;
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
using BEditor.Views;
using BEditor.Views.CustomTitlebars;
using BEditor.Views.DialogContent;

using OpenTK.Audio.OpenAL;

namespace BEditor
{
    public class MainWindow : FluentWindow
    {
        public MainWindow()
        {
            var vm = MainWindowViewModel.Current;
            AddHandler(KeyDownEvent, Window_KeyDown, RoutingStrategies.Tunnel);
            vm.New.Subscribe(CreateProjectClick);

            NotificationManager = new(this)
            {
                Position = NotificationPosition.BottomLeft,
            };

            InitializeComponent();

            vm.IsOpened.Subscribe(v =>
            {
                if (!v && Content is Layoutable layoutable)
                {
                    layoutable.Margin = default;
                }
            });
#if DEBUG
            this.AttachDevTools();
#endif
        }

        public WindowNotificationManager NotificationManager { get; }

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

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}