using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;

using MaterialDesignThemes.Wpf;

using Microsoft.Xaml.Behaviors;

using EventTrigger = Microsoft.Xaml.Behaviors.EventTrigger;
using ClipType = BEditor.Primitive.PrimitiveTypes;
using BEditor.Core.Data;
using Reactive.Bindings;
using System.Windows.Media;

namespace BEditor.ViewModels
{
    public static class CommandTool
    {
        public static EventTrigger CreateEvent(string eventname, ICommand command)
        {
            EventTrigger trigger = new EventTrigger
            {
                EventName = eventname
            };

            InvokeCommandAction action = new()
            {
                Command = command
            };

            trigger.Actions.Add(action);

            return trigger;
        }

        public static EventTrigger CreateEvent(string eventname, ICommand command, IValueConverter converter, object ConverterParameter)
        {
            EventTrigger trigger = new EventTrigger
            {
                EventName = eventname
            };

            InvokeCommandAction action = new InvokeCommandAction()
            {
                Command = command,
                EventArgsConverter = converter,
                EventArgsConverterParameter = ConverterParameter
            };


            trigger.Actions.Add(action);

            return trigger;
        }

        public static EventTrigger CreateEvent(string eventname, ICommand command, object commandparam)
        {
            EventTrigger trigger = new EventTrigger
            {
                EventName = eventname
            };

            InvokeCommandAction action = new InvokeCommandAction()
            {
                Command = command,
                CommandParameter = commandparam
            };


            trigger.Actions.Add(action);

            return trigger;
        }
    }


    public class EventArgsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => (parameter, (EventArgs)value);
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();

        public static EventArgsConverter Converter = new EventArgsConverter();
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
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();

        public static MousePositionConverter Converter = new MousePositionConverter();
    }

    public class ClipTypeColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Type clipType)
            {
                if(Attribute.GetCustomAttribute(clipType, typeof(CustomClipUIAttribute)) is CustomClipUIAttribute att)
                {
                    var c = att.GetColor;
                    return new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
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
            if (value is Type clipType)
            {
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
                else if (clipType == ClipType.Figure)
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
                PackIconKind.Shape => ClipType.FigureMetadata,
                PackIconKind.RoundedCorner => ClipType.RoundRectMetadata,
                PackIconKind.Videocam => ClipType.CameraMetadata,
                PackIconKind.Cube => ClipType.GL3DObjectMetadata,
                PackIconKind.MovieOpen => ClipType.SceneMetadata,
                _ => ClipType.VideoMetadata
            };
        }
    }
}
