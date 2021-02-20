using System;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

using BEditor.Data;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Media;
using BEditor.Media.Encoder;
using BEditor.Properties;
using BEditor.Views;
using BEditor.Views.MessageContent;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

using Reactive.Bindings;

using static BEditor.IMessage;

using Frame = BEditor.Media.Frame;

namespace BEditor.Models
{
    public class OutputModel
    {
        public static readonly OutputModel Current = new();
        private static readonly ILogger logger = AppData.Current.LoggingFactory.CreateLogger<OutputModel>();

        private OutputModel()
        {
            ImageCommand.Where(_ => AppData.Current.Project is not null)
                .Select(_ => AppData.Current.Project!.PreviewScene)
                .Subscribe(OutputImage);

            VideoCommand.Where(_ => AppData.Current.Project is not null)
                .Select(_ => AppData.Current.Project!.PreviewScene)
                .Subscribe(OutputVideo);
        }

        public ReactiveCommand VideoCommand { get; } = new();
        public ReactiveCommand ImageCommand { get; } = new();

        public static async void OutputImage(Scene scene)
        {
            var saveFileDialog = new SaveFileDialog()
            {
                Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico;*.wbmp;*.webp;*.pkm;*.ktx;*.astc;*.dng;*.heif",
                RestoreDirectory = true,
                AddExtension = true
            };

            if (saveFileDialog.ShowDialog() ?? false)
            {
                await OutputImage(saveFileDialog.FileName, scene);
            }
        }
        public static async Task OutputImage(string path, Scene scene)
        {
            int nowframe = scene.PreviewFrame;

            await using var img = scene.Render(nowframe, RenderType.ImageOutput).Image;
            try
            {

                if (img != null)
                {
                    img.Encode(path);
                }
            }
            catch (Exception e)
            {
                await using var prov = AppData.Current.Services.BuildServiceProvider();
                var mes = prov.GetService<IMessage>();
                mes?.Snackbar(MessageResources.FailedToSave);
                logger.LogError(e, MessageResources.FailedToSave);
            }
        }
        public static void OutputVideo(Scene scene)
        {
            var saveFileDialog = new SaveFileDialog()
            {
                Filter = "Video Files|*.mp4;*.avi",
                RestoreDirectory = true,
                AddExtension = true,
            };

            var source = Enum.GetNames(typeof(VideoCodec));
            var codec = new ComboBox()
            {
                ItemsSource = source
            };

            codec.SelectedIndex = 0;

            if (saveFileDialog.ShowDialog() ?? false)
            {
                new NoneDialog(codec).ShowDialog();

                OutputVideo(
                    saveFileDialog.FileName,
                    (VideoCodec)Enum.Parse(typeof(VideoCodec), source[codec.SelectedIndex]),
                    scene);
            }
        }
        public static void OutputVideo(string file, VideoCodec codec, Scene scene)
        {
            var t = false;
            AppData.Current.AppStatus = Status.Output;

            var content = new Loading(new ButtonType[] { ButtonType.Cancel })
            {
                Maximum =
                {
                    Value = scene.TotalFrame
                }
            };
            content.ButtonClicked += (_, _) => t = true;
            var dialog = new NoneDialog(content);
            dialog.Show();

            var thread = new Thread(async () =>
            {
                try
                {
                    var encoder = new FFmpegEncoder(scene.Width, scene.Height, scene.Parent!.Framerate, codec, file);

                    for (Frame frame = 0; frame < scene.TotalFrame; frame++)
                    {
                        if (t) return;
                        content.NowValue.Value = frame;

                        Image<BGRA32>? img = null;

                        dialog.Dispatcher.Invoke(() =>
                        {
                            img = scene.Render(frame, RenderType.VideoOutput).Image;
                        });

                        if (img is not null)
                        {
                            encoder.Write(img);
                            await img.DisposeAsync();
                        }
                    }

                    encoder?.Dispose();

                    dialog.Dispatcher.Invoke(dialog.Close);
                }
                catch (Exception e)
                {
                    await using var prov = AppData.Current.Services.BuildServiceProvider();
                    var mes = prov.GetService<IMessage>();
                    mes?.Snackbar(MessageResources.FailedToSave);
                    logger.LogError(e, MessageResources.FailedToSave);
                }
                finally
                {
                    AppData.Current.AppStatus = Status.Edit;
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }
    }
}