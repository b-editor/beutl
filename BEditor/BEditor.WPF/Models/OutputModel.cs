using System;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;

using BEditor.Core.Data;
using BEditor.Core.Extensions;
using BEditor.Core.Properties;
using BEditor.Core.Service;
using BEditor.Drawing;
using BEditor.Media;
using BEditor.Media.Encoder;
using BEditor.Views;
using BEditor.Views.MessageContent;

using Microsoft.WindowsAPICodePack.Dialogs;
using Microsoft.WindowsAPICodePack.Dialogs.Controls;

using Reactive.Bindings;

namespace BEditor.Models
{
    public class OutputModel
    {
        public static readonly OutputModel Current = new();

        private OutputModel()
        {
            ImageCommand.Where(_ => AppData.Current.Project is not null)
                .Select(_ => AppData.Current.Project.PreviewScene)
                .Subscribe(OutputImage);

            VideoCommand.Where(_ => AppData.Current.Project is not null)
                .Select(_ => AppData.Current.Project.PreviewScene)
                .Subscribe(OutputVideo);
        }

        public ReactiveCommand VideoCommand { get; } = new();
        public ReactiveCommand ImageCommand { get; } = new();

        public static void OutputImage(Scene scene)
        {
            var saveFileDialog = new CommonSaveFileDialog()
            {
                Filters =
                {
                    new("png", "png"),
                    new("jpg", "jpg"),
                    new("jpeg", "jpeg"),
                    new("bmp", "bmp"),
                    new("gif", "gif"),
                    new("ico", "ico"),
                    new("wbmp", "wbmp"),
                    new("webp", "webp"),
                    new("pkm", "pkm"),
                    new("ktx", "ktx"),
                    new("astc", "astc"),
                    new("dng", "dng"),
                    new("heif", "heif"),
                },
                RestoreDirectory = true,
                AlwaysAppendDefaultExtension = true
            };

            if (saveFileDialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                OutputImage(saveFileDialog.FileName, scene);
            }
        }
        public static void OutputImage(string path, Scene scene)
        {
            int nowframe = scene.PreviewFrame;

            using var img = scene.Render(nowframe, RenderType.ImageOutput).Image;
            try
            {

                if (img != null)
                {
                    img.Encode(path);
                }
            }
            catch (Exception e)
            {
                Message.Snackbar($"保存できませんでした : {e.Message}");
            }
        }
        public static void OutputVideo(Scene scene)
        {
            var codec = new CommonFileDialogComboBox("Default");

            foreach (var text in Enum.GetNames(typeof(VideoCodec)))
            {
                codec.Items.Add(new(text));
            }
            codec.SelectedIndex = 0;

            var saveFileDialog = new CommonSaveFileDialog()
            {
                Filters =
                {
                    new("mp4", "mp4"),
                    new("avi", "avi"),
                },
                RestoreDirectory = true,
                AlwaysAppendDefaultExtension = true,
                Controls =
                {
                    codec
                }
            };

            if (saveFileDialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                OutputVideo(
                    saveFileDialog.FileName,
                    (VideoCodec)Enum.Parse(typeof(VideoCodec), codec.Items[codec.SelectedIndex].Text),
                    scene);
            }
        }
        public static void OutputVideo(string file, VideoCodec codec, Scene scene)
        {
            var t = false;
            IVideoEncoder encoder = null;
            AppData.Current.AppStatus = Status.Output;

            try
            {
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

                var thread = new Thread(() =>
                {
                    encoder = new FFmpegEncoder(scene.Width, scene.Height, scene.Parent.Framerate, codec, file);

                    for (Frame frame = 0; frame < scene.TotalFrame; frame++)
                    {
                        if (t) return;
                        content.NowValue.Value = frame;

                        using var img = scene.Render(frame, RenderType.VideoOutput).Image;

                        encoder.Write(img);
                    }

                    dialog.Close();
                });

                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
            }
            catch (Exception e)
            {
                Message.Snackbar($"保存できませんでした : {e.Message}");
            }
            finally
            {
                encoder?.Dispose();
                AppData.Current.AppStatus = Status.Edit;
            }
        }
    }
}