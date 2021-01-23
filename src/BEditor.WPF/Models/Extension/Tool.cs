using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Data;
using BEditor.Media;
using BEditor.ViewModels;

namespace BEditor.Models.Extension
{
    public static class Tool
    {
        public static void PreviewUpdate(this Project project, ClipData clipData, RenderType type = RenderType.Preview)
        {
            if (project is null) return;
            var now = project.PreviewScene.PreviewFrame;
            if (clipData.Start <= now && now <= clipData.End)
            {
                project.PreviewUpdate(type);
            }
        }

        public static void PreviewUpdate(this Project project, RenderType type = RenderType.Preview)
        {
            if (project is null) return;

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

        public static bool Clamp(this Scene self, ClipData? clip_, ref Frame start, ref Frame end, int layer)
        {
            var array = self.GetLayer(layer).ToArray();

            for (int i = 0; i < array.Length; i++)
            {
                ClipData? clip = array[i];

                if (clip != clip_)
                {
                    if (clip.InRange(start, end, out var type))
                    {
                        if (type == RangeType.StartEnd)
                        {
                            return false;
                        }
                        else if (type == RangeType.Start)
                        {
                            start = clip.End;

                            return true;
                        }
                        else if (type == RangeType.End)
                        {
                            end = clip.Start;

                            return true;
                        }


                        return false;
                    }
                }
            }

            return true;
        }
        public static bool InRange(this Scene self, Frame start, Frame end, int layer)
        {
            foreach (var clip in self.GetLayer(layer))
            {
                if (clip.InRange(start, end))
                {
                    return false;
                }
            }

            return true;
        }
        public static bool InRange(this Scene self, ClipData clip_, Frame start, Frame end, int layer)
        {
            var array = self.GetLayer(layer).ToArray();

            for (int i = 0; i < array.Length; i++)
            {
                ClipData? clip = array[i];

                if (clip != clip_)
                {
                    if (clip.InRange(start, end, out _))
                    {
                        return false;
                    }
                }
            }

            return true;
        }
        // このクリップと被る場合はtrue
        public static bool InRange(this ClipData self, Frame start, Frame end)
        {
            //return (self.Start <= start && end <= self.End)
            //    || (start <= self.Start && self.End <= end)
            //    || (self.Start <= start && start <= self.End)
            //    ^ (self.Start <= end && end <= self.End);

            if (self.Start <= start && end <= self.End)
            {
                return true;
            }
            else if (self.Start <= start && start < self.End)
            {
                return true;
            }
            else if (self.Start < end && end <= self.End)
            {
                return true;
            }
            else if (start <= self.Start && self.End <= end)
            {
                return true;
            }

            return false;
        }
        public static bool InRange(this ClipData self, Frame start, Frame end, out RangeType type)
        {
            if (self.Start <= start && end <= self.End)
            {
                type = RangeType.StartEnd;

                return true;
            }
            else if (self.Start <= start && start < self.End)
            {
                type = RangeType.Start;

                return true;
            }
            else if (self.Start < end && end <= self.End)
            {
                type = RangeType.End;

                return true;
            }
            else if (start <= self.Start && self.End <= end)
            {
                type = RangeType.StartEnd;

                return true;
            }
            type = default;
            return false;
        }

        public enum RangeType
        {
            StartEnd,
            Start,
            End
        }
    }
}
