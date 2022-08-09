using BeUtl.Streaming;

namespace BeUtl.Operators;

public class OperatorsRegistrar
{
    public static void RegisterAll()
    {
        OperatorRegistry.RegisterOperations("S.Operators.Source")
            .Add<Source.EllipseOperator>("S.Operators.Source.Ellipse")
            .Add<Source.RectOperator>("S.Operators.Source.Rect")
            .Add<Source.RoundedRectOperation>("S.Operators.Source.RoundedRect")
            .Add<Source.TextBlockOperator>("S.Operators.Source.Text")
            .Add<Source.ImageFileOperator>("S.Operators.Source.ImageFile")
            .Register();

        OperatorRegistry.RegisterOperations("S.Operators.Configure")
            .AddGroup("S.Operators.Configure.Transform", helper => helper
                .Add<Configure.Transform.TranslateOperator>("S.Operators.Configure.Transform.Translate")
                .Add<Configure.Transform.SkewOperator>("S.Operators.Configure.Transform.Skew")
                .Add<Configure.Transform.ScaleOperator>("S.Operators.Configure.Transform.Scale")
                .Add<Configure.Transform.RotationOperator>("S.Operators.Configure.Transform.Rotation")
                .Add<Configure.Transform.Rotation3DOperator>("S.Operators.Configure.Transform.Rotation3D")
                .Register())
            .AddGroup("S.Operators.Configure.ImageFilter", helper => helper
                .Add<Configure.ImageFilter.BlurOperator>("S.Operators.Configure.ImageFilter.Blur")
                .Add<Configure.ImageFilter.BlurOperator>("S.Operators.Configure.ImageFilter.DropShadow")
                .Register())
            .AddGroup("S.Operators.Configure.BitmapEffect", helper => helper
                .Add<Configure.BitmapEffect.BlurOperator>("S.Operators.Configure.BitmapEffect.Blur")
                .Add<Configure.BitmapEffect.InnerShadowOperator>("S.Operators.Configure.BitmapEffect.InnerShadow")
                .Add<Configure.BitmapEffect.BorderOperator>("S.Operators.Configure.BitmapEffect.Border")
                .Register())
            .Add<Configure.AlignmentOperator>("S.Operators.Configure.Alignment")
            .Add<Configure.BlendOperator>("S.Operators.Configure.Blend")
            .Add<Configure.ForegroundOperator>("S.Operators.Configure.Foreground")
            .Add<Configure.OpacityMaskOperator>("S.Operators.Configure.OpacityMask")
            .Register();
    }
}
