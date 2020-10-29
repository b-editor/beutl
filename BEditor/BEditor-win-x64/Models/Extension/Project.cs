using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.ViewModels;

using BEditorCore.Data.ObjectData;

namespace BEditor.Models.Extension {
    public static class Project {
        public static void PreviewUpdate(this BEditorCore.Data.ProjectData.Project project, ClipData clipData) {
            var now = project.PreviewScene.PreviewFrame;
            if (clipData.Start <= now && now <= clipData.End) {
                project.PreviewUpdate();
            }
        }

        public static void PreviewUpdate(this BEditorCore.Data.ProjectData.Project project) {
            using (var img = project.PreviewScene.Rendering()) {
                MainWindowViewModel.Current.PreviewImage.Value = img.ToBitmapSource();
            }
        }
    }
}
