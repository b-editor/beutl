using Avalonia.Input;
using Beutl.Services;

namespace Beutl.Editor.Components;

public static class BeutlDataFormats
{
    private const string ElementFormat = "BeutlElementJson";
    private const string ElementsFormat = "BeutlElementsJson";

    public static readonly DataFormat<string> Element = DataFormat.CreateStringApplicationFormat(ElementFormat);
    public static readonly DataFormat<string> Elements = DataFormat.CreateStringApplicationFormat(ElementsFormat);
    public static readonly DataFormat<string> KeyFrame = DataFormat.CreateStringApplicationFormat(nameof(Animation.KeyFrame));
    public static readonly DataFormat<string> KeyFrameAnimation = DataFormat.CreateStringApplicationFormat(nameof(Animation.KeyFrameAnimation));
    public static readonly DataFormat<string> SourceOperator = DataFormat.CreateStringApplicationFormat(KnownLibraryItemFormats.SourceOperator);
    public static readonly DataFormat<string> Node = DataFormat.CreateStringApplicationFormat(KnownLibraryItemFormats.Node);
    public static readonly DataFormat<string> Drawable = DataFormat.CreateStringApplicationFormat(KnownLibraryItemFormats.Drawable);
    public static readonly DataFormat<string> Sound = DataFormat.CreateStringApplicationFormat(KnownLibraryItemFormats.Sound);
    public static readonly DataFormat<string> Transform = DataFormat.CreateStringApplicationFormat(KnownLibraryItemFormats.Transform);
    public static readonly DataFormat<string> FilterEffect = DataFormat.CreateStringApplicationFormat(KnownLibraryItemFormats.FilterEffect);
    public static readonly DataFormat<string> AudioEffect = DataFormat.CreateStringApplicationFormat(KnownLibraryItemFormats.AudioEffect);
    public static readonly DataFormat<string> Brush = DataFormat.CreateStringApplicationFormat(KnownLibraryItemFormats.Brush);
    public static readonly DataFormat<string> Easing = DataFormat.CreateStringApplicationFormat(KnownLibraryItemFormats.Easing);
    public static readonly DataFormat<string> Geometry = DataFormat.CreateStringApplicationFormat(KnownLibraryItemFormats.Geometry);
}
