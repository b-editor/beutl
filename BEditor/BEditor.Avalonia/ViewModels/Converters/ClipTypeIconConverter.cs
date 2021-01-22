using System;
using System.Globalization;

using Avalonia.Data.Converters;
using Avalonia.Media;

using BEditor.Core.Data;
using BEditor.Media;

namespace BEditor.ViewModels.Converters
{
    public class ClipTypeIconConverter : IValueConverter
    {
        private static readonly Geometry Video = PathGeometry.Parse("M18,4L20,8H17L15,4H13L15,8H12L10,4H8L10,8H7L5,4H4A2,2 0 0,0 2,6V18A2,2 0 0,0 4,20H20A2,2 0 0,0 22,18V4H18Z");
        private static readonly Geometry Image = PathGeometry.Parse("M8.5,13.5L11,16.5L14.5,12L19,18H5M21,19V5C21,3.89 20.1,3 19,3H5A2,2 0 0,0 3,5V19A2,2 0 0,0 5,21H19A2,2 0 0,0 21,19Z");
        private static readonly Geometry Text = PathGeometry.Parse("M14,17H7V15H14M17,13H7V11H17M17,9H7V7H17M19,3H5C3.89,3 3,3.89 3,5V19A2,2 0 0,0 5,21H19A2,2 0 0,0 21,19V5C21,3.89 20.1,3 19,3Z");
        private static readonly Geometry Figure = PathGeometry.Parse("M11,13.5V21.5H3V13.5H11M12,2L17.5,11H6.5L12,2M17.5,13C20,13 22,15 22,17.5C22,20 20,22 17.5,22C15,22 13,20 13,17.5C13,15 15,13 17.5,13Z");
        private static readonly Geometry RoundRect = PathGeometry.Parse("M19,19H21V21H19V19M19,17H21V15H19V17M3,13H5V11H3V13M3,17H5V15H3V17M3,9H5V7H3V9M3,5H5V3H3V5M7,5H9V3H7V5M15,21H17V19H15V21M11,21H13V19H11V21M15,21H17V19H15V21M7,21H9V19H7V21M3,21H5V19H3V21M21,8A5,5 0 0,0 16,3H11V5H16A3,3 0 0,1 19,8V13H21V8Z");
        private static readonly Geometry Camera = PathGeometry.Parse("M17,10.5V7A1,1 0 0,0 16,6H4A1,1 0 0,0 3,7V17A1,1 0 0,0 4,18H16A1,1 0 0,0 17,17V13.5L21,17.5V6.5L17,10.5Z");
        private static readonly Geometry GLObject = PathGeometry.Parse("M21,16.5C21,16.88 20.79,17.21 20.47,17.38L12.57,21.82C12.41,21.94 12.21,22 12,22C11.79,22 11.59,21.94 11.43,21.82L3.53,17.38C3.21,17.21 3,16.88 3,16.5V7.5C3,7.12 3.21,6.79 3.53,6.62L11.43,2.18C11.59,2.06 11.79,2 12,2C12.21,2 12.41,2.06 12.57,2.18L20.47,6.62C20.79,6.79 21,7.12 21,7.5V16.5M12,4.15L6.04,7.5L12,10.85L17.96,7.5L12,4.15Z");
        private static readonly Geometry Scene = PathGeometry.Parse("M20.84 2.18L16.91 2.96L19.65 6.5L21.62 6.1L20.84 2.18M13.97 3.54L12 3.93L14.75 7.46L16.71 7.07L13.97 3.54M9.07 4.5L7.1 4.91L9.85 8.44L11.81 8.05L9.07 4.5M4.16 5.5L3.18 5.69A2 2 0 0 0 1.61 8.04L2 10L6.9 9.03L4.16 5.5M2 10V20C2 21.11 2.9 22 4 22H20C21.11 22 22 21.11 22 20V10H2Z");
        private static readonly Geometry None = PathGeometry.Parse("");

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Type clipType)
            {
                if (clipType == ClipType.Video)
                {
                    return Video;
                }
                else if (clipType == ClipType.Image)
                {
                    return Image;
                }
                else if (clipType == ClipType.Text)
                {
                    return Text;
                }
                else if (clipType == ClipType.Figure)
                {
                    return Figure;
                }
                else if (clipType == ClipType.RoundRect)
                {
                    return RoundRect;
                }
                else if (clipType == ClipType.Camera)
                {
                    return Camera;
                }
                else if (clipType == ClipType.GL3DObject)
                {
                    return GLObject;
                }
                else if (clipType == ClipType.Scene)
                {
                    return Scene;
                }
                else
                {
                    return None;
                }
            }
            else
            {
                return None;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
