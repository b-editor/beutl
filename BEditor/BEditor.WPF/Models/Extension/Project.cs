using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.ViewModels;

using BEditor.ObjectModel.ObjectData;

namespace BEditor.Models.Extension
{
    public static class Project
    {
        public static void PreviewUpdate(this BEditor.ObjectModel.ProjectData.Project project, ClipData clipData)
        {
            var now = project.PreviewScene.PreviewFrame;
            if (clipData.Start <= now && now <= clipData.End)
            {
                project.PreviewUpdate();
            }
        }

        public static void PreviewUpdate(this BEditor.ObjectModel.ProjectData.Project project)
        {
            var img = project.PreviewScene.Rendering().Image;
            MainWindowViewModel.Current.PreviewImage.Value = img.ToBitmapSource();
        }
    }
}
