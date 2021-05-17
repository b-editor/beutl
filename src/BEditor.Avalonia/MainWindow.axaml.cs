using System;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Reactive.Linq;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

using BEditor.Data;
using BEditor.Media;
using BEditor.Models;
using BEditor.Properties;
using BEditor.ViewModels;
using BEditor.ViewModels.DialogContent;
using BEditor.Views.DialogContent;

using Microsoft.Extensions.Logging;

using OpenTK.Audio.OpenAL;

namespace BEditor
{
    public class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            // WindowsŠÂ‹«‚¾‚Æ•\Ž¦‚ªƒoƒO‚é‚Ì‚Å‘Îô
            MainWindowViewModel.Current.IsOpened
                .ObserveOn(AvaloniaScheduler.Instance)
                .Where(_ => OperatingSystem.IsWindows())
                .Subscribe(isopened =>
            {
                var content = (Grid)Content!;
                if (isopened)
                {
                    content.Margin = new(0, 0, 8, 0);
                }
                else
                {
                    content.Margin = default;
                }
            });
#if DEBUG
            this.AttachDevTools();
#endif
        }
        public record Record(float Value);
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

        public async void CreateProjectClick(object s, RoutedEventArgs e)
        {
            var viewmodel = new CreateProjectViewModel();
            var dialog = new CreateProject
            {
                DataContext = viewmodel
            };

            await dialog.ShowDialog((Window)VisualRoot);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        protected override async void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            await CheckOpenALAsync();
        }

        private static async Task CheckOpenALAsync()
        {
            try
            {
                _ = AL.GetError();
            }
            catch
            {
                await AppModel.Current.Message.DialogAsync(Strings.OpenALNotFound);
                App.Shutdown(1);
            }
        }

        private async Task InstallFFmpegWindowsAsync(FFmpegInstaller installer)
        {
            var msg = AppModel.Current.Message;

            try
            {
                ProgressDialog dialog = null!;

                void start(object? s, EventArgs e)
                {
                    Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        dialog = new ProgressDialog
                        {
                            Maximum = { Value = 100 },
                            Minimum = { Value = 0 }
                        };

                        dialog.Show();

                        dialog.Text.Value = Strings.DownloadingRequiredComponents;
                    });
                }
                void downloadComp(object? s, AsyncCompletedEventArgs e)
                {
                    dialog.Text.Value = Strings.ComponentIsExtractedAndPlaced;
                    dialog.IsIndeterminate.Value = true;
                }
                void progress(object? s, DownloadProgressChangedEventArgs e)
                {
                    dialog.NowValue.Value = e.ProgressPercentage;
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