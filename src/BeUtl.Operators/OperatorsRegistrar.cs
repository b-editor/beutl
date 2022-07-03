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
            .Register();

        OperatorRegistry.RegisterOperations("S.Opearations.Configure")
            .AddGroup("S.Opearations.Configure.Transform", helper => helper
                .Add<Configure.Transform.TranslateOperator>("S.Opearations.Configure.Transform.Translate")
                .Register())
            .Register();
    }
}
