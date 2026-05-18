using Beutl.Services.Tutorials;

namespace Beutl.Services.PrimitiveImpls;

[PrimitiveImpl]
public class DefaultTutorialExtension : TutorialExtension
{
    public static readonly DefaultTutorialExtension Instance = new();

    public override IReadOnlyList<TutorialDefinition> GetTutorials()
    {
        return
        [
            WelcomeTutorial.Create(),
            TimelineBasicsTutorial.Create(),
            AnimationEditTutorial.Create(),
        ];
    }
}
