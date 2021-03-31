using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

using BEditor.Models;
using BEditor.Properties;

using MahApps.Metro.Controls;

using Microsoft.Extensions.Logging;

namespace BEditor.Views.Setup
{
    /// <summary>
    /// Setup.xaml の相互作用ロジック
    /// </summary>
    public partial class Setup : MetroWindow
    {
        public Setup()
        {
            InitializeComponent();

            Task.Run(SetupCore);
        }

        private async Task SetupCore()
        {
            await InstallFFmpeg();
            await InstallOpenAL();

            ClearInline();
            AddInline(Strings.SetupComplete);

            await Task.Delay(5000);

            await ((App)Application.Current).StartupCore();

            GC.Collect();

            Dispatcher.Invoke(Close);

            Settings.Default.SetupFlag = true;
        }
        private async Task InstallOpenAL()
        {
            var installer = new OpenALInstaller();
            var msg = AppData.Current.Message;

            if (!OpenALInstaller.IsInstalled())
            {
                var oalColor = Color.FromRgb(39, 157, 221);
                AddInline("OpenAL\n", oalColor);
                AddInline("is downloading.");
                SetProgressbar(oalColor);
                SetProgressbar(false);

                try
                {
                    void downloadComp(object? s, AsyncCompletedEventArgs e)
                    {
                        ClearInline();
                        AddInline("Install");
                        AddInline("OpenAL\n", oalColor);
                        AddInline("from the dialog that starts.");
                        SetProgressbar(true);
                    }
                    void progress(object s, DownloadProgressChangedEventArgs e)
                    {
                        SetProgressbar(e.ProgressPercentage);
                    }

                    installer.DownloadCompleted += downloadComp;
                    installer.DownloadProgressChanged += progress;

                    await installer.Install();

                    installer.DownloadCompleted -= downloadComp;
                    installer.DownloadProgressChanged -= progress;
                }
                catch (Exception e)
                {
                    msg.Dialog(string.Format(Strings.FailedToInstall, "OpenAL"));

                    App.Logger?.LogError(e, "Failed to install OpenAL.");
                }

                ClearInline();
            }
        }
        private async Task InstallFFmpeg()
        {
            var ffmpegDir = System.IO.Path.Combine(AppContext.BaseDirectory, "ffmpeg");
            var installer = new FFmpegInstaller(ffmpegDir);
            var msg = AppData.Current.Message;

            if (!await installer.IsInstalledAsync())
            {
                var ffmpegColor = Color.FromRgb(0, 200, 83);
                AddInline("FFmpeg\n", ffmpegColor);
                AddInline("is downloading.");
                SetProgressbar(ffmpegColor);
                SetProgressbar(false);

                try
                {
                    void downloadComp(object? s, AsyncCompletedEventArgs e)
                    {
                        ClearInline();
                        AddInline("FFmpeg\n", ffmpegColor);
                        AddInline("is extracted and placed.");
                        SetProgressbar(true);
                    }
                    void progress(object s, DownloadProgressChangedEventArgs e)
                    {
                        SetProgressbar(e.ProgressPercentage);
                    }

                    installer.DownloadCompleted += downloadComp;
                    installer.DownloadProgressChanged += progress;

                    await installer.Install();

                    installer.DownloadCompleted -= downloadComp;
                    installer.DownloadProgressChanged -= progress;
                }
                catch (Exception e)
                {
                    msg.Dialog(string.Format(Strings.FailedToInstall, "FFmpeg"));

                    App.Logger?.LogError(e, "Failed to install ffmpeg.");
                }

                ClearInline();
            }
        }
        private void SetProgressbar(bool value)
        {
            progress.Dispatcher.InvokeAsync(() =>
            {
                progress.IsIndeterminate = value;
            });
        }
        private void SetProgressbar(Color color)
        {
            progress.Dispatcher.InvokeAsync(() =>
            {
                var forebrush = new SolidColorBrush(color);
                progress.Foreground = forebrush;

                var back = forebrush.Clone();
                back.Opacity = 0.25;
                progress.BorderBrush = back;
                progress.Background = back;
            });
        }
        private void SetProgressbar(double value)
        {
            progress.Dispatcher.InvokeAsync(() =>
            {
                progress.Value = value;
            });
        }
        private void AddInline(string text)
        {
            Text_Block.Dispatcher.InvokeAsync(() =>
            {
                Text_Block.Inlines.Add(new Run()
                {
                    Text = text
                });
            });
        }
        private void AddInline(string text, Color color)
        {
            Text_Block.Dispatcher.InvokeAsync(() =>
            {
                Text_Block.Inlines.Add(new Run()
                {
                    Text = text,
                    Foreground = new SolidColorBrush(color)
                });
            });
        }
        private void ClearInline()
        {
            Dispatcher.InvokeAsync(() =>
            {
                Text_Block.Inlines.Clear();
            });
        }
    }
}
