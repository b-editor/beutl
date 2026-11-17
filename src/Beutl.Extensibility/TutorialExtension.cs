using Microsoft.Extensions.DependencyInjection;
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
            Services.GetRequiredService<ITutorialService>().Register(tutorial);
        }
    }

    public override void Unload()
    {
        foreach (TutorialDefinition tutorial in GetTutorials())
        {
            Services.GetRequiredService<ITutorialService>().Unregister(tutorial.Id);
        }
        base.Unload();
    }
}
