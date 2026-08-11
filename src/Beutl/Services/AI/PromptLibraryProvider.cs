namespace Beutl.Services.AI;

internal static class PromptLibraryProvider
{
    private static readonly Lazy<IPromptLibrary> s_current = new(() =>
        new PersistentPromptLibrary(
            Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), "ai-prompts.json")));

    public static IPromptLibrary Current => s_current.Value;
}
