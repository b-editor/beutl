using System;
using System.ComponentModel;
using System.Net;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

using BEditor.Models;
using BEditor.Properties;
using BEditor.ViewModels.DialogContent;
using BEditor.Views;
using BEditor.Views.DialogContent;

using Microsoft.Extensions.Logging;

namespace BEditor
{
    public class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
#if DEBUG
            this.AttachDevTools();
#endif
        }

        public void Button_Click(object s, RoutedEventArgs e)
        {

        }

        public async void ShowSettings(object s, RoutedEventArgs e)
        {
            await new SettingsWindow().ShowDialog(this);
        }

        public async void CreateProjectClick(object s, RoutedEventArgs e)
        {
            var viewmodel = new CreateProjectViewModel();
            var content = new CreateProject
            {
                DataContext = viewmodel
            };
            var dialog = new EmptyDialog(content);

            await dialog.ShowDialog(this);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        protected override async void OnOpened(EventArgs e)
        {
            var installer = new FFmpegInstaller(App.FFmpegDir);

            if (!await installer.IsInstalledAsync())
            {
                if (OperatingSystem.IsWindows())
                {
                    await InstallFFmpegWindowsAsync(installer);
                }
                else if (OperatingSystem.IsLinux())
                {
                    await AppModel.Current.Message.DialogAsync($"{Strings.RunFollowingCommandToInstallFFmpeg}\n$ brew install ffmpeg");

                    Close();
                }
                else if (OperatingSystem.IsMacOS())
                {
                    await AppModel.Current.Message.DialogAsync($"{Strings.RunFollowingCommandToInstallFFmpeg}\n$ sudo apt update\n$ sudo apt -y upgrade\n$ sudo apt install ffmpeg");

                    Close();
                }
            }
            base.OnOpened(e);
        }

        private async Task InstallFFmpegWindowsAsync(FFmpegInstaller installer)
        {
            var msg = AppModel.Current.Message;

            try
            {
                Loading loading = null!;
                EmptyDialog dialog = null!;

                void start(object? s, EventArgs e)
                {
                    Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        loading = new Loading
                        {
                            Maximum = { Value = 100 },
                            Minimum = { Value = 0 }
                        };
                        dialog = new EmptyDialog(loading);

                        dialog.Show();

                        loading.Text.Value = string.Format(Strings.IsDownloading, "FFmpeg");
                    });
                }
                void downloadComp(object? s, AsyncCompletedEventArgs e)
                {
                    loading.Text.Value = string.Format(Strings.IsExtractedAndPlaced, "FFmpeg");
                    loading.IsIndeterminate.Value = true;
                }
                void progress(object s, DownloadProgressChangedEventArgs e)
                {
                    loading.NowValue.Value = e.ProgressPercentage;
                }
                void installed(object? s, EventArgs e) => Dispatcher.UIThread.InvokeAsync(dialog.Close);

                installer.StartInstall += start;
                installer.Installed += installed;
                installer.DownloadCompleted += downloadComp;
                installer.DownloadProgressChanged += progress;

                await installer.InstallAsync();

                installer.StartInstall -= start;
                installer.Installed -= installed;
                installer.DownloadCompleted -= downloadComp;
                installer.DownloadProgressChanged -= progress;
            }
            catch (Exception e)
            {
                await msg.DialogAsync(string.Format(Strings.FailedToInstall, "FFmpeg"));

                App.Logger?.LogError(e, "Failed to install ffmpeg.");
            }
        }
    }
}
