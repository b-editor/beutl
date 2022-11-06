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
        if (property.Id == AnimationSpan.EasingProperty.Id)
        {
            return S.Common.EasingObservable;
        }
        else if (property.Id == Layer.StartProperty.Id
            || property.Id == LayerNode.StartProperty.Id)
        {
            return S.Common.StartTimeObservable;
        }
        else if (property.Id == AnimationSpan.DurationProperty.Id
            || property.Id == Scene.DurationProperty.Id
            || property.Id == Layer.LengthProperty.Id
            || property.Id == LayerNode.DurationProperty.Id)
        {
            return S.Common.DurationTimeObservable;
        }
        else if (property.Id == Graphics.Effects.OpenCv.Blur.KernelSizeProperty.Id
            || property.Id == Graphics.Effects.OpenCv.GaussianBlur.KernelSizeProperty.Id
            || property.Id == Graphics.Effects.OpenCv.MedianBlur.KernelSizeProperty.Id
            || property.Id == Graphics.Effects.InnerShadow.KernelSizeProperty.Id)
        {
            return S.Common.KernelSizeObservable;
        }
        else if (property.Id == Graphics.Effects.OpenCv.Blur.FixImageSizeProperty.Id
            || property.Id == Graphics.Effects.OpenCv.GaussianBlur.FixImageSizeProperty.Id
            || property.Id == Graphics.Effects.OpenCv.MedianBlur.FixImageSizeProperty.Id)
        {
            return S.Common.FixImageSizeObservable;
        }
        else if (property.Id == Graphics.Filters.Blur.SigmaProperty.Id
            || property.Id == Graphics.Filters.DropShadow.SigmaProperty.Id
            || property.Id == Graphics.Effects.OpenCv.GaussianBlur.SigmaProperty.Id)
        {
            return S.Common.SigmaObservable;
        }
        else if (property.Id == Graphics.Filters.DropShadow.PositionProperty.Id
            || property.Id == Graphics.Effects.InnerShadow.PositionProperty.Id)
        {
            return S.Common.PositionObservable;
        }
        else if (property.Id == Graphics.Filters.DropShadow.ColorProperty.Id
            || property.Id == Graphics.Effects.Border.ColorProperty.Id
            || property.Id == Graphics.Effects.InnerShadow.ColorProperty.Id
            || property.Id == Media.SolidColorBrush.ColorProperty.Id)
        {
            return S.Common.ColorObservable;
        }
        else if (property.Id == Graphics.Filters.DropShadow.ShadowOnlyProperty.Id)
        {
            return S.Common.ShadowOnlyObservable;
        }
        else if (property.Id == Graphics.Effects.Border.OffsetProperty.Id)
        {
            return S.Common.OffsetObservable;
        }
        else if (property.Id == Graphics.Effects.Border.ThicknessProperty.Id)
        {
            return S.Common.ThicknessObservable;
        }
        else if (property.Id == Graphics.Effects.Border.MaskTypeProperty.Id)
        {
            return S.Common.MaskTypeObservable;
        }
        else if (property.Id == Graphics.Effects.Border.StyleProperty.Id)
        {
            return S.Common.BorderStyleObservable;
        }
        else if (property.Id == Graphics.Shapes.Ellipse.StrokeWidthProperty.Id
            || property.Id == Graphics.Shapes.Rectangle.StrokeWidthProperty.Id
            || property.Id == Graphics.Shapes.RoundedRect.StrokeWidthProperty.Id)
        {
            return S.Common.StrokeWidthObservable;
        }
        else if (property.Id == Graphics.Shapes.RoundedRect.CornerRadiusProperty.Id)
        {
            return S.Common.CornerRadiusObservable;
        }
        else if (property.Id == Graphics.Shapes.TextBlock.FontFamilyProperty.Id
            || property.Id == Graphics.Shapes.TextElement.FontFamilyProperty.Id)
        {
            return S.Common.FontFamilyObservable;
        }
        else if (property.Id == Graphics.Shapes.TextBlock.FontWeightProperty.Id
            || property.Id == Graphics.Shapes.TextElement.FontWeightProperty.Id)
        {
            return S.Common.FontWeightObservable;
        }
        else if (property.Id == Graphics.Shapes.TextBlock.FontStyleProperty.Id
            || property.Id == Graphics.Shapes.TextElement.FontStyleProperty.Id)
        {
            return S.Common.FontStyleObservable;
        }
        else if (property.Id == Graphics.Shapes.TextBlock.SizeProperty.Id
            || property.Id == Graphics.Shapes.TextElement.SizeProperty.Id)
        {
            return S.Common.SizeObservable;
        }
        else if (property.Id == Graphics.Shapes.TextBlock.SpacingProperty.Id
            || property.Id == Graphics.Shapes.TextElement.SpacingProperty.Id)
        {
            return S.Common.CharactorSpacingObservable;
        }
        else if (property.Id == Graphics.Shapes.TextBlock.TextProperty.Id
            || property.Id == Graphics.Shapes.TextElement.TextProperty.Id)
        {
            return S.Common.TextObservable;
        }
        else if (property.Id == Graphics.Shapes.TextBlock.MarginProperty.Id
            || property.Id == Graphics.Shapes.TextElement.MarginProperty.Id)
        {
            return S.Common.MarginObservable;
        }
        else if (property.Id == Graphics.Drawable.WidthProperty.Id)
        {
            return S.Common.WidthObservable;
        }
        else if (property.Id == Graphics.Drawable.HeightProperty.Id)
        {
            return S.Common.HeightObservable;
        }
        else if (property.Id == Graphics.Drawable.TransformProperty.Id
            || property.Id == Media.Brush.TransformProperty.Id)
        {
            return S.Common.TransformObservable;
        }
        else if (property.Id == Graphics.Drawable.TransformOriginProperty.Id
            || property.Id == Media.Brush.TransformOriginProperty.Id)
        {
            return S.Common.TransformOriginObservable;
        }
        else if (property.Id == Graphics.Drawable.FilterProperty.Id)
        {
            return S.Common.ImageFilterObservable;
        }
        else if (property.Id == Graphics.Drawable.EffectProperty.Id)
        {
            return S.Common.BitmapEffectObservable;
        }
        else if (property.Id == Graphics.Drawable.AlignmentXProperty.Id
            || property.Id == Media.TileBrush.AlignmentXProperty.Id)
        {
            return S.Common.AlignmentXObservable;
        }
        else if (property.Id == Graphics.Drawable.AlignmentYProperty.Id
            || property.Id == Media.TileBrush.AlignmentYProperty.Id)
        {
            return S.Common.AlignmentYObservable;
        }
        else if (property.Id == Graphics.Drawable.ForegroundProperty.Id)
        {
            return S.Common.ForegroundObservable;
        }
        else if (property.Id == Graphics.Drawable.OpacityMaskProperty.Id)
        {
            return S.Common.OpacityMaskObservable;
        }
        else if (property.Id == Graphics.Drawable.BlendModeProperty.Id)
        {
            return S.Common.BlendModeObservable;
        }
        else if (property.Id == Graphics.ImageFile.SourceFileProperty.Id)
        {
            return S.Common.SourceFileObservable;
        }
        else if (property.Id == Media.Brush.OpacityProperty.Id)
        {
            return S.Common.OpacityObservable;
        }
        else if (property.Id == Media.ConicGradientBrush.CenterProperty.Id
            || property.Id == Media.RadialGradientBrush.CenterProperty.Id)
        {
            return S.Common.CenterObservable;
        }
        else if (property.Id == Media.ConicGradientBrush.AngleProperty.Id)
        {
            return S.Common.AngleObservable;
        }
        else if (property.Id == Media.GradientBrush.SpreadMethodProperty.Id)
        {
            return S.Common.SpreadMethodObservable;
        }
        else if (property.Id == Media.GradientBrush.GradientStopsProperty.Id)
        {
            return S.Common.GradientStopsObservable;
        }
        else if (property.Id == Media.LinearGradientBrush.StartPointProperty.Id)
        {
            return S.Common.StartPointObservable;
        }
        else if (property.Id == Media.LinearGradientBrush.EndPointProperty.Id)
        {
            return S.Common.EndPointObservable;
        }
        else if (property.Id == Media.RadialGradientBrush.GradientOriginProperty.Id)
        {
            return S.Common.GradientOriginObservable;
        }
        else if (property.Id == Media.RadialGradientBrush.RadiusProperty.Id)
        {
            return S.Common.RadiusObservable;
        }
        else if (property.Id == Media.TileBrush.DestinationRectProperty.Id)
        {
            return S.Common.DestinationRectObservable;
        }
        else if (property.Id == Media.TileBrush.SourceRectProperty.Id)
        {
            return S.Common.SourceRectObservable;
        }
        else if (property.Id == Media.TileBrush.StretchProperty.Id)
        {
            return S.Common.StretchObservable;
        }
        else if (property.Id == Media.TileBrush.TileModeProperty.Id)
        {
            return S.Common.TileModeObservable;
        }
        else if (property.Id == Media.TileBrush.BitmapInterpolationModeProperty.Id)
        {
            return S.Common.BitmapInterpolationModeObservable;
        }
        else
        {
            return null;
        }
    }
}
