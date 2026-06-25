using Beutl.Services.Tutorials;

namespace Beutl.Services.PrimitiveImpls;

[PrimitiveImpl]
public class DefaultTutorialExtension : TutorialExtension
{
    private readonly EditorService _editorService;
    private readonly ProjectService _projectService;

    // The editor-session services are constructor-injected from the composition root so the
    // tutorial step bodies can drive the editor instead of the removed EditorService.Current /
    // ProjectService.Current singletons. Unlike the other primitive extensions this one cannot
    // be a service-free static singleton, so LoadPrimitiveExtensionTask constructs it.
    public DefaultTutorialExtension(EditorService editorService, ProjectService projectService)
    {
        ArgumentNullException.ThrowIfNull(editorService);
        ArgumentNullException.ThrowIfNull(projectService);

        _editorService = editorService;
        _projectService = projectService;
    }

    public override IReadOnlyList<TutorialDefinition> GetTutorials()
    {
        return
        [
            WelcomeTutorial.Create(),
            TimelineBasicsTutorial.Create(_editorService, _projectService),
            AnimationEditTutorial.Create(_editorService, _projectService)
        ];
    }
}
