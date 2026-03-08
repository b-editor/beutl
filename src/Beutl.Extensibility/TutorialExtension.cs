using Beutl.Services.Tutorials;

namespace Beutl.Extensibility;

public abstract class TutorialExtension : Extension
{
    public abstract IReadOnlyList<TutorialDefinition> GetTutorials();

    public override void Load()
    {
        base.Load();
        foreach (TutorialDefinition tutorial in GetTutorials())
        {
            TutorialService.Current.Register(tutorial);
        }
    }

    public override void Unload()
    {
        foreach (TutorialDefinition tutorial in GetTutorials())
        {
            TutorialService.Current.Unregister(tutorial.Id);
        }
        base.Unload();
    }
}
