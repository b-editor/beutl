using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows.Data;

using BEditor.Data;
using BEditor.Properties;

using MaterialDesignThemes.Wpf;

namespace BEditor.ViewModels.Converters
{
    public sealed class AppStatusIconConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Status status)
            {
                return status switch
                {
                    Status.Idle => PackIconKind.ContainStart,
                    Status.Edit => PackIconKind.Edit,
                    Status.Saved => PackIconKind.ContentSaveEdit,
                    Status.Playing => PackIconKind.Play,
                    Status.Output => PackIconKind.Output,
                    _ => default,
                };
            }
            else return null;
        }

        public object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }

    public sealed class AppStatusToPlayerIconConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Status status)
            {

                if (status is Status.Playing) return PackIconKind.Pause;
                else return PackIconKind.PlayArrow;
            }
            else return null;
        }

        public object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }
    public sealed class AppStatusTextConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Status status)
            {
                return status switch
                {
                    Status.Idle => "",
                    Status.Edit => Strings.Edit,
                    Status.Saved => Strings.FileSaved,
                    Status.Playing => Strings.Playing,
                    Status.Output => Strings.Outputs,
                    _ => throw new NotImplementedException(),
                };
            }
            else return null;
        }

        public object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }
}