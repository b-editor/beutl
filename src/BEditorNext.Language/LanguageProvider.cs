namespace BEditorNext.Language;

public class LanguageProvider
{
    public LanguageProvider()
    {
        CommandName = new CommandNameProvider();
    }

    public static LanguageProvider Instance { get; set; } = new();

    public CommandNameProvider CommandName { get; set; }
}
