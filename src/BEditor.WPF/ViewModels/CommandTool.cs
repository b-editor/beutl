using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

using BEditor.Data;
using BEditor.Models.Extension;

using MaterialDesignThemes.Wpf;

using Microsoft.Xaml.Behaviors;

using Reactive.Bindings;

using ClipType = BEditor.Primitive.PrimitiveTypes;
using EventTrigger = Microsoft.Xaml.Behaviors.EventTrigger;

namespace BEditor.ViewModels
{
    public class EventArgsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (parameter, (EventArgs)value);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        public static readonly EventArgsConverter Converter = new();
    }

    public class MousePositionConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is MouseEventArgs a)
            {
                return a.GetPosition((IInputElement)parameter);
            }
            return null;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        public static readonly MousePositionConverter Converter = new();
    }

    public class ClipTypeColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Type clipType)
            {
                foreach (var item in ObjectMetadata.LoadedObjects)
                {
                    if (item.Type == clipType)
                    {
                        return item.AccentColor.ToBrush();
                    }
                }

                return new SolidColorBrush(Color.FromRgb(48, 79, 238));
            }
            else
            {
                return new SolidColorBrush(Color.FromRgb(48, 79, 238));
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null!;
        }
    }
    public class ClipTypeIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            Type? clipType;

            if (value is Type type)
            {
                clipType = type;
            }
            else if (value is ClipElement clip)
            {
                clipType = clip.Effect[0].GetType();
            }
            else
            {
                return PackIconKind.None;
            }

            if (clipType == ClipType.Video)
            {
                return PackIconKind.Movie;
            }
            else if (clipType == ClipType.Image)
            {
                return PackIconKind.Image;
            }
            else if (clipType == ClipType.Text)
            {
                return PackIconKind.TextBox;
            }
            else if (clipType == ClipType.Shape)
            {
                return PackIconKind.Shape;
            }
            else if (clipType == ClipType.RoundRect)
            {
                return PackIconKind.RoundedCorner;
            }
            else if (clipType == ClipType.Camera)
            {
                return PackIconKind.Videocam;
            }
            else if (clipType == ClipType.GL3DObject)
            {
                return PackIconKind.Cube;
            }
            else if (clipType == ClipType.Scene)
            {
                return PackIconKind.MovieOpen;
            }
            else
            {
                return PackIconKind.None;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is PackIconKind kind)
            {
                return ToClipType(kind);
            }

            return ClipType.Video;
        }

        public static Type ToClipType(PackIconKind kind)
        {
            return ToClipMetadata(kind).Type;
        }
        public static ObjectMetadata ToClipMetadata(PackIconKind kind)
        {
            return kind switch
            {
                PackIconKind.Movie => ClipType.VideoMetadata,
                PackIconKind.Audio => ClipType.AudioMetadata,
                PackIconKind.Image => ClipType.ImageMetadata,
                PackIconKind.TextBox => ClipType.TextMetadata,
                PackIconKind.Shape => ClipType.ShapeMetadata,
                PackIconKind.RoundedCorner => ClipType.RoundRectMetadata,
                PackIconKind.Videocam => ClipType.CameraMetadata,
                PackIconKind.Cube => ClipType.GL3DObjectMetadata,
                PackIconKind.MovieOpen => ClipType.SceneMetadata,
                _ => ClipType.VideoMetadata
            };
        }
    }
}