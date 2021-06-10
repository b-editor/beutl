using System.ComponentModel;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

using BEditor.ViewModels.DialogContent;
using BEditor.ViewModels.Start;
using BEditor.Views.DialogContent;

namespace BEditor.Views.Start
{
    public sealed class Projects : UserControl
    {
        public Projects()
        {
            InitializeComponent();

            if (DataContext is ProjectsViewModel vm)
            {
                vm.OpenItem.Subscribe(async _ =>
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        var main = new MainWindow();
                        App.SetMainWindow(main);
                        main.Show();
                        if (VisualRoot is Window win) win.Close();
                    });
                });
            }
        }

        public void CreateNew(object s, RoutedEventArgs e)
        {
            if (VisualRoot is Window parent)
            {
                var dialog = new CreateProject { DataContext = new CreateProjectViewModel() };
                App.SetMainWindow(dialog);
                dialog.Show();
                parent.Close();

                dialog.Closing += Dialog_Closing;
            }
        }

        private void Dialog_Closing(object? sender, CancelEventArgs e)
        {
            if (sender is Window window)
            {
                var main = new MainWindow();
                App.SetMainWindow(main);
                main.Show();

                window.Closing -= Dialog_Closing;
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}