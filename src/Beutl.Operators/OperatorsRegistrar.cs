using Beutl.Language;
using Beutl.Operation;

namespace Beutl.Operators;

public class OperatorsRegistrar
{
    public static void RegisterAll()
    {
        OperatorRegistry.RegisterOperations(Strings.Source)
            .Add<Source.EllipseOperator>(Strings.Ellipse)
            .Add<Source.RectOperator>(Strings.Rectangle)
            .Add<Source.RoundedRectOperator>(Strings.RoundedRect)
            .Add<Source.TextBlockOperator>(Strings.Text)
            .Add<Source.VideoFrameOperator>("Video")
            .Add<Source.SourceVideoOperator>("SourceVideo")
            .Add<Source.SourceImageOperator>("SourceImage")
            .Add<Source.SourceSoundOperator>("SourceSound")
            .Register();

        OperatorRegistry.RegisterOperations(Strings.Configure)
            .AddGroup(Strings.Transform, helper => helper
                .Add<Configure.Transform.TranslateOperator>(Strings.Translate)
                .Add<Configure.Transform.SkewOperator>(Strings.Skew)
                .Add<Configure.Transform.ScaleOperator>(Strings.Scale)
                .Add<Configure.Transform.RotationOperator>(Strings.Rotation)
                .Add<Configure.Transform.Rotation3DOperator>(Strings.Rotation3D)
                .Register())
            .AddGroup(Strings.ImageFilter, helper => helper
                .Add<Configure.Effects.BlurOperator>(Strings.Blur)
                .Add<Configure.Effects.DropShadowOperator>(Strings.DropShadow)
                .Add<Configure.Effects.CvBlurOperator>(Strings.Blur)
                .Add<Configure.Effects.InnerShadowOperator>(Strings.InnerShadow)
                .Add<Configure.Effects.BorderOperator>(Strings.Border)
                .Register())
            .AddGroup("SoundEffect", helper => helper
                .Add<Configure.SoundEffect.DelayOperator>("Delay")
                .Register())
            .Add<Configure.AlignmentOperator>(Strings.Alignment)
            .Add<Configure.BlendOperator>(Strings.BlendMode)
            .Add<Configure.ForegroundOperator>(Strings.Foreground)
            .Add<Configure.OpacityMaskOperator>(Strings.OpacityMask)
            .Register();
    }
}
