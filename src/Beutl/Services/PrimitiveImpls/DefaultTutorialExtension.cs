using Beutl.Services.Tutorials;

namespace Beutl.Services.PrimitiveImpls;

public class DefaultTutorialExtension : TutorialExtension
{
    private readonly EditorService _editorService;
    private readonly ProjectService _projectService;

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
