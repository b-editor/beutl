using BeUtl.Streaming;

namespace BeUtl.Operators;

public class OperatorsRegistrar
{
    public static void RegisterAll()
    {
        OperatorRegistry.RegisterOperations("S.Opearations.Source")
            .Add<Source.EllipseOperator>("S.Opearations.Source.Ellipse")
            .Add<Source.RectOperator>("S.Opearations.Source.Rect")
            .Add<Source.RoundedRectOperation>("S.Opearations.Source.RoundedRect")
            .Add<Source.TextBlockOperator>("S.Opearations.Source.Text")
            .Register();

        OperatorRegistry.RegisterOperations("S.Opearations.Configure")
            .AddGroup("S.Opearations.Configure.Transform", helper => helper
                .Add<Configure.Transform.TranslateOperator>("S.Opearations.Configure.Transform.Translate")
                .Add<Configure.Transform.SkewOperator>("S.Opearations.Configure.Transform.Skew")
                .Add<Configure.Transform.ScaleOperator>("S.Opearations.Configure.Transform.Scale")
                .Add<Configure.Transform.RotationOperator>("S.Opearations.Configure.Transform.Rotation")
                .Add<Configure.Transform.Rotation3DOperator>("S.Opearations.Configure.Transform.Rotation3D")
                .Register())
            .AddGroup("S.Opearations.Configure.ImageFilter", helper => helper
                .Add<Configure.ImageFilter.BlurOperator>("S.Opearations.Configure.ImageFilter.Blur")
                .Add<Configure.ImageFilter.BlurOperator>("S.Opearations.Configure.ImageFilter.DropShadow")
                .Register())
            .AddGroup("S.Opearations.Configure.BitmapEffect", helper => helper
                .Add<Configure.BitmapEffect.BlurOperator>("S.Opearations.Configure.BitmapEffect.Blur")
                .Add<Configure.BitmapEffect.InnerShadowOperator>("S.Opearations.Configure.BitmapEffect.InnerShadow")
                .Add<Configure.BitmapEffect.BorderOperator>("S.Opearations.Configure.BitmapEffect.Border")
                .Register())
            .Add<Configure.AlignmentOperator>("S.Opearations.Configure.Alignment")
            .Add<Configure.BlendOperator>("S.Opearations.Configure.Blend")
            .Add<Configure.ForegroundOperator>("S.Opearations.Configure.Foreground")
            .Add<Configure.OpacityMaskOperator>("S.Opearations.Configure.OpacityMask")
            .Register();
    }
}
