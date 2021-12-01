namespace BEditorNext.Language;

public class CommandNameProvider
{
    public static CommandNameProvider Instance => LanguageProvider.Instance.CommandName;

    public virtual string AddRenderTask => "Add a render task.";

    public virtual string RemoveRenderTask => "Remove a render task.";
}
