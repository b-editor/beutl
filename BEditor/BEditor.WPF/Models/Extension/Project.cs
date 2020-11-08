using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.ViewModels;

using BEditor.Core.Data.ObjectData;

namespace BEditor.Models.Extension {
    public static class Project {
        public static void PreviewUpdate(this BEditor.Core.Data.ProjectData.Project project, ClipData clipData) {
            var now = project.PreviewScene.PreviewFrame;
            if (clipData.Start <= now && now <= clipData.End) {
                project.PreviewUpdate();
            }
        }

        public static void PreviewUpdate(this BEditor.Core.Data.ProjectData.Project project) {
            var img = project.PreviewScene.Rendering();
            MainWindowViewModel.Current.PreviewImage.Value = img.ToBitmapSource();
        }
    }
}
