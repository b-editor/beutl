using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;

using BEditor.Core.Data;
using BEditor.Core.Extensions;
using BEditor.Core.Extensions.ViewCommand;
using BEditor.Core.Properties;
using BEditor.Core.Service;
using BEditor.Drawing;
using BEditor.Media;
using BEditor.Media.Encoder;
using BEditor.Views;
using BEditor.Views.MessageContent;

using Microsoft.WindowsAPICodePack.Dialogs;
using Microsoft.WindowsAPICodePack.Dialogs.Controls;

namespace BEditor.Models
{
    public static class ImageHelper
    {
        #region フレームの画像出力
        /// <summary>
        /// フレームを画像出力
        /// </summary>
        public static void OutputImage(string path)
        {

            int nowframe = AppData.Current.Project.PreviewScene.PreviewFrame;

            using var img = AppData.Current.Project.PreviewScene.Render(nowframe, RenderType.ImageOutput).Image;
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

        /// <summary>
        /// フレームを画像出力
        /// </summary>
        public static void OutputImage()
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
                OutputImage(saveFileDialog.FileName);
            }
        }
        #endregion

        public static void OutputVideo()
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
                OutputVideo(saveFileDialog.FileName, (VideoCodec)Enum.Parse(typeof(VideoCodec), codec.Items[codec.SelectedIndex].Text));
            }
        }
        public static void OutputVideo(string file, VideoCodec codec)
        {
            var proj = AppData.Current.Project;
            var scn = proj.PreviewScene;
            var t = false;
            IVideoEncoder encoder = null;
            AppData.Current.AppStatus = Status.Output;

            try
            {
                var content = new Loading(new ButtonType[] { ButtonType.Cancel })
                {
                    Maximum =
                        {
                            Value = scn.TotalFrame
                        }
                };
                content.ButtonClicked += (_, _) => t = true;
                var dialog = new NoneDialog(content);
                dialog.Show();

                var thread = new Thread(() =>
                {
                    encoder = new FFmpegEncoder(scn.Width, scn.Height, proj.Framerate, codec, file);

                    for (Frame frame = 0; frame < scn.TotalFrame; frame++)
                    {
                        if (t) return;
                        content.NowValue.Value = frame;

                        using var img = scn.Render(frame, RenderType.VideoOutput).Image;

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