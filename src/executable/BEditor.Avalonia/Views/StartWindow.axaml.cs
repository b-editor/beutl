using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

using BEditor.Models;

namespace BEditor.Views
{
    public sealed class StartWindow : FluentWindow
    {
        private readonly TabControl _tabControl;

        public StartWindow()
        {
            InitializeComponent();
            _tabControl = this.FindControl<TabControl>("tabControl");
#if DEBUG
            this.AttachDevTools();
#endif
        }

        public async void TabChanged(object s, SelectionChangedEventArgs e)
        {
            if (_tabControl?.SelectedItem is TabItem item)
            {
                if (item.Tag is "Settings")
                {
                    var old = (TabItem)e.RemovedItems[0]!;
                    await new SettingsWindow().ShowDialog(this);

                    _tabControl.SelectedItem = old;
                }
                else if (item.Tag is "MainWindow")
                {
                    var main = new MainWindow();
                    App.SetMainWindow(main);
                    main.Show();
                    Close();
                }
            }
        }

        protected override async void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            await App.StartupTask;
            App.StartupTask = default;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}