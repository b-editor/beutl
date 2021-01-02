using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Data;
using BEditor.ViewModels;

namespace BEditor.Models.Extension
{
    public static class Project
    {
        public static void PreviewUpdate(this Core.Data.Project project, ClipData clipData, RenderType type = RenderType.Preview)
        {
            var now = project.PreviewScene.PreviewFrame;
            if (clipData.Start <= now && now <= clipData.End)
            {
                project.PreviewUpdate(type);
            }
        }

        public static void PreviewUpdate(this Core.Data.Project project, RenderType type = RenderType.Preview)
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                using var img = project.PreviewScene.Render(type).Image;
                var outimg = MainWindowViewModel.Current.PreviewImage.Value;

                if (outimg is null || outimg.Width != img.Width || outimg.Height != img.Height)
                {
                    MainWindowViewModel.Current.PreviewImage.Value = new(
                        img.Width,
                        img.Height,
                        96,
                        96,
                        System.Windows.Media.PixelFormats.Bgra32,
                        null);
                }

                //96,

                BitmapSourceConverter.ToWriteableBitmap(img, MainWindowViewModel.Current.PreviewImage.Value);
            });
        }
    }
}
