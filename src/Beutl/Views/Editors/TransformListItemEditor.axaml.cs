using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Beutl.ViewModels.Editors;

namespace Beutl.Views.Editors;

public partial class TransformListItemEditor : PresenterListItemEditor
{
    public TransformListItemEditor()
    {
        Resources["TransformTypeToIconConverter"] = TransformTypeToIconConverter.Instance;
        InitializeComponent();
        AttachParts(reorderHandle, content, presenterEditor);
    }

    private sealed class TransformTypeToIconConverter : IValueConverter
    {
        public static readonly TransformTypeToIconConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            string? key = value is KnownTransformType type ? TransformEditorViewModel.Find(type)?.IconKey : null;
            return Application.Current!.FindResource(key ?? "TransformListItemEditor_ReOrderIcon");
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return null;
        }
    }
}
