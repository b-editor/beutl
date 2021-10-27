using System;
using System.IO;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

using BEditor.Controls;
using BEditor.ViewModels.Dialogs;

namespace BEditor.Views.Dialogs
{
    public sealed class CreateProjectPackage : FluentWindow
    {
        public CreateProjectPackage()
        {
            var vm = new CreateProjectPackageViewModel();
            vm.Create.Subscribe(() => Dispatcher.UIThread.InvokeAsync(Close));
            vm.OpenOtherFile.Subscribe(async () =>
            {
                if (DataContext is not CreateProjectPackageViewModel vm) return;
                var dialog = new OpenFileDialog
                {
                    AllowMultiple = true,
                };
                var files = await dialog.ShowAsync(this);

                if (files == null) return;

                foreach (var item in files)
                {
                    var exist = false;
                    foreach (var file in vm.Others)
                    {
                        if (file.Hint == item)
                        {
                            exist = true;
                        }
                    }

                    if (!exist)
                    {
                        vm.Others.Add(new CreateProjectPackageViewModel.TreeItem(Path.GetFileName(item), item, item));
                    }
                }
            });
            vm.OpenFolderDialog.Subscribe(async () =>
            {
                if (DataContext is not CreateProjectPackageViewModel vm) return;
                var dialog = new OpenFolderDialog();
                var folder = await dialog.ShowAsync(App.GetMainWindow());

                if (Directory.Exists(folder) && folder != null)
                {
                    vm.Folder.Value = folder;
                    var settings = BEditor.Settings.Default;

                    settings.LastTimeFolder = folder;

                    settings.Save();
                }
            });

            DataContext = vm;
            InitializeComponent();
#if DEBUG
            this.AttachDevTools();
#endif
        }

        public void CloseClick(object s, RoutedEventArgs e)
        {
            Close();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}