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
        public static void PreviewUpdate(this Core.Data.Project project, ClipData clipData)
        {
            var now = project.PreviewScene.PreviewFrame;
            if (clipData.Start <= now && now <= clipData.End)
            {
                project.PreviewUpdate();
            }
        }

        public static void PreviewUpdate(this Core.Data.Project project)
        {
            using var img = project.PreviewScene.Render().Image;
            MainWindowViewModel.Current.PreviewImage.Value = img.ToBitmapSource();
        }
    }
}
