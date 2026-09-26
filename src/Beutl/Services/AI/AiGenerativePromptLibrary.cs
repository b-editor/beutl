using Beutl.Api.Services;
using Beutl.Language;
using Beutl.Logging;
using Beutl.NodeGraph.Generative;
using Microsoft.Extensions.Logging;

namespace Beutl.Services.AI;

/// <summary>
/// The dialogs' prompt library as the node graph sees it: the same account-scoped store,
/// the same task kinds and the same ordering.
/// </summary>
internal sealed class AiGenerativePromptLibrary(IPromptLibrary library) : IGenerativePromptLibrary
{
    private static readonly ILogger s_logger = Log.CreateLogger<AiGenerativePromptLibrary>();

    public IReadOnlyList<GenerativePromptEntry> GetEntries(GenerativeOperation operation)
    {
        PromptTaskKind kind = ToTaskKind(operation);
        try
        {
            return
            [
                .. library.Templates
                    .Where(item => item.TaskKind == kind)
                    .OrderByDescending(item => item.IsPinned)
                    .ThenByDescending(item => item.UpdatedAtUtc)
                    .Select(item => new GenerativePromptEntry(item.Name, item.Prompt, true, item.IsPinned)),
                .. library.History
                    .Where(item => item.TaskKind == kind)
                    .OrderByDescending(item => item.IsPinned)
                    .ThenByDescending(item => item.LastUsedAtUtc)
                    .Select(item => new GenerativePromptEntry(Summarize(item.Prompt), item.Prompt, false, item.IsPinned)),
            ];
        }
        catch (Exception ex) when (IsLibraryFailure(ex))
        {
            s_logger.LogWarning(ex, "Failed to read the prompt library.");
            return [];
        }
    }

    public void Record(GenerativeOperation operation, string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            return;
        try
        {
            library.Record(ToTaskKind(operation), prompt);
        }
        catch (Exception ex) when (IsLibraryFailure(ex))
        {
            // The generation succeeded; a history the account cannot write is not worth failing it.
            s_logger.LogWarning(ex, "Failed to record prompt history.");
        }
    }

    public string? SaveTemplate(GenerativeOperation operation, string name, string prompt)
    {
        try
        {
            library.SaveTemplate(name, ToTaskKind(operation), prompt);
            return null;
        }
        catch (AuthenticationRequiredException)
        {
            return Strings.AiAuthenticationRequired;
        }
        catch (ArgumentException)
        {
            return Strings.AiPromptTemplateInvalid;
        }
        catch (Exception ex) when (IsLibraryFailure(ex))
        {
            s_logger.LogWarning(ex, "Failed to save a prompt template.");
            return Strings.AiResultUnavailable;
        }
    }

    private static PromptTaskKind ToTaskKind(GenerativeOperation operation) => operation switch
    {
        GenerativeOperation.ImageGeneration => PromptTaskKind.Image,
        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
    };

    private static string Summarize(string prompt)
    {
        string summary = prompt.Split('\n', 2)[0];
        return summary.Length > 72 ? summary[..69] + "…" : summary;
    }

    private static bool IsLibraryFailure(Exception ex)
        => ex is AuthenticationRequiredException
            or IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or NotSupportedException;
}
