using BeUtl.Streaming;

using static BeUtl.Language.Resources;

namespace BeUtl.Operators;

public class OperatorsRegistrar
{
    public static void RegisterAll()
    {
        OperatorRegistry.RegisterOperations(S.Operators.IndexSourceObservable)
            .Add<Source.EllipseOperator>(S.Operators.Source.EllipseObservable)
            .Add<Source.RectOperator>(S.Operators.Source.RectObservable)
            .Add<Source.RoundedRectOperation>(S.Operators.Source.RoundedRectObservable)
            .Add<Source.TextBlockOperator>(S.Operators.Source.TextObservable)
            .Add<Source.ImageFileOperator>(S.Operators.Source.ImageFileObservable)
            .Register();

        OperatorRegistry.RegisterOperations(S.Operators.IndexConfigureObservable)
            .AddGroup(S.Operators.Configure.IndexTransformObservable, helper => helper
                .Add<Configure.Transform.TranslateOperator>(S.Operators.Configure.Transform.TranslateObservable)
                .Add<Configure.Transform.SkewOperator>(S.Operators.Configure.Transform.SkewObservable)
                .Add<Configure.Transform.ScaleOperator>(S.Operators.Configure.Transform.ScaleObservable)
                .Add<Configure.Transform.RotationOperator>(S.Operators.Configure.Transform.RotationObservable)
                .Add<Configure.Transform.Rotation3DOperator>(S.Operators.Configure.Transform.Rotation3DObservable)
                .Register())
            .AddGroup(S.Operators.Configure.IndexImageFilterObservable, helper => helper
                .Add<Configure.ImageFilter.BlurOperator>(S.Operators.Configure.ImageFilter.BlurObservable)
                .Add<Configure.ImageFilter.BlurOperator>(S.Operators.Configure.ImageFilter.DropShadowObservable)
                .Register())
            .AddGroup(S.Operators.Configure.IndexBitmapEffectObservable, helper => helper
                .Add<Configure.BitmapEffect.BlurOperator>(S.Operators.Configure.BitmapEffect.BlurObservable)
                .Add<Configure.BitmapEffect.InnerShadowOperator>(S.Operators.Configure.BitmapEffect.InnerShadowObservable)
                .Add<Configure.BitmapEffect.BorderOperator>(S.Operators.Configure.BitmapEffect.BorderObservable)
                .Register())
            .Add<Configure.AlignmentOperator>(S.Operators.Configure.AlignmentObservable)
            .Add<Configure.BlendOperator>(S.Operators.Configure.BlendObservable)
            .Add<Configure.ForegroundOperator>(S.Operators.Configure.ForegroundObservable)
            .Add<Configure.OpacityMaskOperator>(S.Operators.Configure.OpacityMaskObservable)
            .Register();
    }
}
