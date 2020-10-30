using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows.Data;
using BEditor.NET.Data;
using BEditor.NET.Properties;
using MaterialDesignThemes.Wpf;

namespace BEditor.ViewModels.Converters {
    public class AppStatusIconConverter : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            if (value is Status status) {
                return status switch
                {
                    Status.Idle => PackIconKind.ContainStart,
                    Status.Edit => PackIconKind.Edit,
                    Status.Saved => PackIconKind.ContentSaveEdit,
                    Status.Playing => PackIconKind.Play,
                    Status.Pause => PackIconKind.Pause,
                    Status.Output => PackIconKind.Output,
                    _ => throw new NotImplementedException(),
                };
            }
            else return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            return null;
        }
    }

    public class AppStatusTextConverter : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            if (value is Status status) {
                return status switch
                {
                    Status.Idle => "",
                    Status.Edit => Resources.Edit,
                    Status.Saved => Resources.FileSaved,
                    Status.Playing => "",
                    Status.Pause => "",
                    Status.Output => Resources.Outputs,
                    _ => throw new NotImplementedException(),
                };
            }
            else return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            return null;
        }
    }
}
