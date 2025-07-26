using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;
using Beutl.ViewModels;
using FluentIcons.Common;

namespace Beutl.Views;

public partial class FileExplorer : UserControl
{
    public FileExplorer()
    {
        InitializeComponent();
    }
}

namespace Beutl.ViewModels
{
    public class FileIconConverter : IValueConverter
    {
        public static readonly FileIconConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        {
            if (value is FileSystemItemViewModel item)
            {
                if (item.IsDirectory)
                {
                    return Symbol.Folder;
                }
                
                // 拡張子別アイコン
                return item.Extension?.ToLowerInvariant() switch
                {
                    ".cs" or ".csx" => Symbol.Code,
                    ".json" or ".xml" or ".config" => Symbol.DataBarVertical,
                    ".txt" or ".md" => Symbol.DocumentText,
                    ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".svg" => Symbol.Image,
                    ".mp4" or ".avi" or ".mov" or ".mkv" => Symbol.Video,
                    ".mp3" or ".wav" or ".ogg" or ".m4a" => Symbol.MusicNote2,
                    ".zip" or ".rar" or ".7z" or ".tar" => Symbol.FolderZip,
                    ".exe" or ".dll" => Symbol.Apps,
                    ".pdf" => Symbol.DocumentPdf,
                    _ => Symbol.Document
                };
            }
            else if (value is bool isDirectory && isDirectory)
            {
                return Symbol.Folder;
            }
            return Symbol.Document;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class FileSizeConverter : IValueConverter
    {
        public static readonly FileSizeConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        {
            if (value is long size)
            {
                if (size < 1024)
                    return $"{size} B";
                else if (size < 1024 * 1024)
                    return $"{size / 1024.0:F1} KB";
                else if (size < 1024 * 1024 * 1024)
                    return $"{size / (1024.0 * 1024.0):F1} MB";
                else
                    return $"{size / (1024.0 * 1024.0 * 1024.0):F1} GB";
            }
            return string.Empty;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
    
    public class DisplayModeConverter : IValueConverter
    {
        public static readonly DisplayModeConverter Instance = new();
        
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is FileExplorerDisplayMode mode && parameter is string targetMode)
            {
                return mode.ToString() == targetMode;
            }
            return false;
        }
        
        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
    
    public class DisplayModeIconConverter : IValueConverter
    {
        public static readonly DisplayModeIconConverter Instance = new();
        
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is FileExplorerDisplayMode mode)
            {
                return mode switch
                {
                    FileExplorerDisplayMode.List => Symbol.List,
                    FileExplorerDisplayMode.Icons => Symbol.Grid,
                    FileExplorerDisplayMode.Tree => Symbol.TextBulletListTree,
                    _ => Symbol.List
                };
            }
            return Symbol.List;
        }
        
        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}