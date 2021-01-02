using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using BEditor.Core.Data;
using BEditor.Drawing;
using BEditor.Media;
using BEditor.Shared;

namespace BEditor.Models
{
    public static class Extensions
    {
        const int width = 10;
        public static readonly string PreviewImage = Path.Combine(AppContext.BaseDirectory, "PreviewImage.png");

        public static double ToPixel(this Scene self, int number)
            => width * (self.TimeLineZoom / 200) * number;
        public static Frame ToFrame(this Scene self, double pixel)
            => (Frame)(pixel / (width * (self.TimeLineZoom / 200)));

        public static void PreviewUpdate(this Project project, ClipData clip)
        {
            var now = project.PreviewScene.PreviewFrame;
            if (clip.Start <= now && now <= clip.End)
            {
                project.PreviewUpdate();
            }
        }
        public static void PreviewUpdate(this Project project)
        {
            AppData.Current.ProjectThread.Post(_ =>
            {
                using var img = project.PreviewScene.Render().Image;

                img.Encode(PreviewImage);
                MainLayout.Current.Image.Update();
            }, null);
        }
    }
}
