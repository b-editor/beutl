using Beutl.Animation;
using Beutl.Framework;
using Beutl.ProjectSystem;
using Beutl.Rendering;

namespace Beutl.Services.PrimitiveImpls;

[PrimitiveImpl]
public sealed class DefaultPropertyNameExtension : LocalizedPropertyNameExtension
{
    public static readonly DefaultPropertyNameExtension Instance = new();

    public override string Name => "Default Property Name";

    public override string DisplayName => "Default Property Name";

    public override IObservable<string>? GetLocalizedName(CoreProperty property)
    {
        string? str = GetLocalizedNameCore(property);

        return str != null ? Observable.Return(str) : null;
    }

    private static string? GetLocalizedNameCore(CoreProperty property)
    {
        if (property.Id == AnimationSpan.EasingProperty.Id)
        {
            return Strings.Easing;
        }
        else if (property.Id == Layer.StartProperty.Id
            || property.Id == LayerNode.StartProperty.Id)
        {
            return Strings.StartTime;
        }
        else if (property.Id == AnimationSpan.DurationProperty.Id
            || property.Id == Scene.DurationProperty.Id
            || property.Id == Layer.LengthProperty.Id
            || property.Id == LayerNode.DurationProperty.Id)
        {
            return Strings.DurationTime;
        }
        else if (property.Id == Graphics.Effects.OpenCv.Blur.KernelSizeProperty.Id
            || property.Id == Graphics.Effects.OpenCv.GaussianBlur.KernelSizeProperty.Id
            || property.Id == Graphics.Effects.OpenCv.MedianBlur.KernelSizeProperty.Id
            || property.Id == Graphics.Effects.InnerShadow.KernelSizeProperty.Id)
        {
            return Strings.KernelSize;
        }
        else if (property.Id == Graphics.Effects.OpenCv.Blur.FixImageSizeProperty.Id
            || property.Id == Graphics.Effects.OpenCv.GaussianBlur.FixImageSizeProperty.Id
            || property.Id == Graphics.Effects.OpenCv.MedianBlur.FixImageSizeProperty.Id)
        {
            return Strings.FixImageSize;
        }
        else if (property.Id == Graphics.Filters.Blur.SigmaProperty.Id
            || property.Id == Graphics.Filters.DropShadow.SigmaProperty.Id
            || property.Id == Graphics.Effects.OpenCv.GaussianBlur.SigmaProperty.Id)
        {
            return Strings.Sigma;
        }
        else if (property.Id == Graphics.Filters.DropShadow.PositionProperty.Id
            || property.Id == Graphics.Effects.InnerShadow.PositionProperty.Id)
        {
            return Strings.Position;
        }
        else if (property.Id == Graphics.Filters.DropShadow.ColorProperty.Id
            || property.Id == Graphics.Effects.Border.ColorProperty.Id
            || property.Id == Graphics.Effects.InnerShadow.ColorProperty.Id
            || property.Id == Media.SolidColorBrush.ColorProperty.Id)
        {
            return Strings.Color;
        }
        else if (property.Id == Graphics.Filters.DropShadow.ShadowOnlyProperty.Id)
        {
            return Strings.ShadowOnly;
        }
        else if (property.Id == Graphics.Effects.Border.OffsetProperty.Id)
        {
            return Strings.Offset;
        }
        else if (property.Id == Graphics.Effects.Border.ThicknessProperty.Id)
        {
            return Strings.Thickness;
        }
        else if (property.Id == Graphics.Effects.Border.MaskTypeProperty.Id)
        {
            return Strings.MaskType;
        }
        else if (property.Id == Graphics.Effects.Border.StyleProperty.Id)
        {
            return Strings.BorderStyle;
        }
        else if (property.Id == Graphics.Shapes.Ellipse.StrokeWidthProperty.Id
            || property.Id == Graphics.Shapes.Rectangle.StrokeWidthProperty.Id
            || property.Id == Graphics.Shapes.RoundedRect.StrokeWidthProperty.Id)
        {
            return Strings.StrokeWidth;
        }
        else if (property.Id == Graphics.Shapes.RoundedRect.CornerRadiusProperty.Id)
        {
            return Strings.CornerRadius;
        }
        else if (property.Id == Graphics.Shapes.TextBlock.FontFamilyProperty.Id
            || property.Id == Graphics.Shapes.TextElement.FontFamilyProperty.Id)
        {
            return Strings.FontFamily;
        }
        else if (property.Id == Graphics.Shapes.TextBlock.FontWeightProperty.Id
            || property.Id == Graphics.Shapes.TextElement.FontWeightProperty.Id)
        {
            return Strings.FontWeight;
        }
        else if (property.Id == Graphics.Shapes.TextBlock.FontStyleProperty.Id
            || property.Id == Graphics.Shapes.TextElement.FontStyleProperty.Id)
        {
            return Strings.FontStyle;
        }
        else if (property.Id == Graphics.Shapes.TextBlock.SizeProperty.Id
            || property.Id == Graphics.Shapes.TextElement.SizeProperty.Id)
        {
            return Strings.Size;
        }
        else if (property.Id == Graphics.Shapes.TextBlock.SpacingProperty.Id
            || property.Id == Graphics.Shapes.TextElement.SpacingProperty.Id)
        {
            return Strings.CharactorSpacing;
        }
        else if (property.Id == Graphics.Shapes.TextBlock.TextProperty.Id
            || property.Id == Graphics.Shapes.TextElement.TextProperty.Id)
        {
            return Strings.Text;
        }
        else if (property.Id == Graphics.Shapes.TextBlock.MarginProperty.Id
            || property.Id == Graphics.Shapes.TextElement.MarginProperty.Id)
        {
            return Strings.Margin;
        }
        else if (property.Id == Graphics.Drawable.WidthProperty.Id)
        {
            return Strings.Width;
        }
        else if (property.Id == Graphics.Drawable.HeightProperty.Id)
        {
            return Strings.Height;
        }
        else if (property.Id == Graphics.Drawable.TransformProperty.Id
            || property.Id == Media.Brush.TransformProperty.Id)
        {
            return Strings.Transform;
        }
        else if (property.Id == Graphics.Drawable.TransformOriginProperty.Id
            || property.Id == Media.Brush.TransformOriginProperty.Id)
        {
            return Strings.TransformOrigin;
        }
        else if (property.Id == Graphics.Drawable.FilterProperty.Id)
        {
            return Strings.ImageFilter;
        }
        else if (property.Id == Graphics.Drawable.EffectProperty.Id)
        {
            return Strings.BitmapEffect;
        }
        else if (property.Id == Graphics.Drawable.AlignmentXProperty.Id
            || property.Id == Media.TileBrush.AlignmentXProperty.Id)
        {
            return Strings.AlignmentX;
        }
        else if (property.Id == Graphics.Drawable.AlignmentYProperty.Id
            || property.Id == Media.TileBrush.AlignmentYProperty.Id)
        {
            return Strings.AlignmentY;
        }
        else if (property.Id == Graphics.Drawable.ForegroundProperty.Id)
        {
            return Strings.Foreground;
        }
        else if (property.Id == Graphics.Drawable.OpacityMaskProperty.Id)
        {
            return Strings.OpacityMask;
        }
        else if (property.Id == Graphics.Drawable.BlendModeProperty.Id)
        {
            return Strings.BlendMode;
        }
        else if (property.Id == Graphics.ImageFile.SourceFileProperty.Id)
        {
            return Strings.SourceFile;
        }
        else if (property.Id == Media.Brush.OpacityProperty.Id)
        {
            return Strings.Opacity;
        }
        else if (property.Id == Media.ConicGradientBrush.CenterProperty.Id
            || property.Id == Media.RadialGradientBrush.CenterProperty.Id)
        {
            return Strings.Center;
        }
        else if (property.Id == Media.ConicGradientBrush.AngleProperty.Id)
        {
            return Strings.Angle;
        }
        else if (property.Id == Media.GradientBrush.SpreadMethodProperty.Id)
        {
            return Strings.SpreadMethod;
        }
        else if (property.Id == Media.GradientBrush.GradientStopsProperty.Id)
        {
            return Strings.GradientStops;
        }
        else if (property.Id == Media.LinearGradientBrush.StartPointProperty.Id)
        {
            return Strings.StartPoint;
        }
        else if (property.Id == Media.LinearGradientBrush.EndPointProperty.Id)
        {
            return Strings.EndPoint;
        }
        else if (property.Id == Media.RadialGradientBrush.GradientOriginProperty.Id)
        {
            return Strings.GradientOrigin;
        }
        else if (property.Id == Media.RadialGradientBrush.RadiusProperty.Id)
        {
            return Strings.Radius;
        }
        else if (property.Id == Media.TileBrush.DestinationRectProperty.Id)
        {
            return Strings.DestinationRect;
        }
        else if (property.Id == Media.TileBrush.SourceRectProperty.Id)
        {
            return Strings.SourceRect;
        }
        else if (property.Id == Media.TileBrush.StretchProperty.Id)
        {
            return Strings.Stretch;
        }
        else if (property.Id == Media.TileBrush.TileModeProperty.Id)
        {
            return Strings.TileMode;
        }
        else if (property.Id == Media.TileBrush.BitmapInterpolationModeProperty.Id)
        {
            return Strings.BitmapInterpolationMode;
        }
        else
        {
            return null;
        }
    }
}
