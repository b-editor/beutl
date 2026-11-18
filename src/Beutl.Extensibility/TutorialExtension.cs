using Beutl.Services.Tutorials;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Extensibility;

public abstract class TutorialExtension : Extension
{
    public abstract IReadOnlyList<TutorialDefinition> GetTutorials();

    public override void Load(IExtensionContext context)
    {
        base.Load(context);
        foreach (TutorialDefinition tutorial in GetTutorials())
        {
            context.Services.GetRequiredService<ITutorialService>().Register(tutorial);
        }
    }

    public override void Unload(IExtensionContext context)
    {
        foreach (TutorialDefinition tutorial in GetTutorials())
        {
            context.Services.GetRequiredService<ITutorialService>().Unregister(tutorial.Id);
        }
        base.Unload(context);
    }
}
