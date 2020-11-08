using System;
using System.Windows;
using System.Windows.Media.Imaging;

using BEditor.Core.Data;
using BEditor.Core.Extensions;
using BEditor.Core.Extensions.ViewCommand;
using BEditor.Core.Media;
using BEditor.Core.Properties;

using Microsoft.WindowsAPICodePack.Dialogs;

namespace BEditor.Models {
    internal static class ImageHelper {
        #region フレームの画像出力
        /// <summary>
        /// フレームを画像出力
        /// </summary>
        internal static void Output_Image(in string path) {

            int nowframe = Component.Current.Project.PreviewScene.PreviewFrame;

            Image img = Component.Current.Project.PreviewScene.Rendering(nowframe);

            try {

                if (img != null) {
                    //img.SaveImage(path);
                    img.Save(path);

                    img.Dispose();
                }
            }
            catch (Exception e) {
                Message.Snackbar($"保存できませんでした : {e.Message}");
            }
        }

        /// <summary>
        /// フレームを画像出力
        /// </summary>
        internal static void OutputImage() {
            CommonSaveFileDialog saveFileDialog = new CommonSaveFileDialog();
            saveFileDialog.Filters.Add(new CommonFileDialogFilter(Resources.ImageFile, "png,jpeg,jpg,bmp"));
            saveFileDialog.RestoreDirectory = true;

            if (saveFileDialog.ShowDialog() == CommonFileDialogResult.Ok) {
                Output_Image(saveFileDialog.FileName);
            }
        }
        #endregion



        #region UIElement_Renderer
        internal static BitmapSource RenderToBitmap(this UIElement element, System.Windows.Size size) {
            try {
                element.Measure(size);
                element.Arrange(new System.Windows.Rect(size));
                element.UpdateLayout();

                var bitmap = new RenderTargetBitmap(
                    (int)size.Width, (int)size.Height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);

                bitmap.Render(element);
                return bitmap;
            }
            catch (Exception e) {
                ActivityLog.ErrorLog(e);
            }

            return null;
        }
        #endregion
    }
}