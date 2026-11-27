namespace Beutl.Agents;

public enum AgentComposerSuggestionKind { Command, Skill }

public sealed record AgentComposerSuggestion(
    string Name, string? Description, AgentComposerSuggestionKind Kind, string? ArgumentHint = null, string? Source = null);

public sealed record AgentComposerSuggestionRequest(
    AgentProviderKind Provider,
    string ExecutablePath,
    string WorkingDirectory,
    IReadOnlyList<string>? Arguments = null,
    IReadOnlyDictionary<string, string>? Environment = null,
    Guid? ThreadId = null);

public readonly record struct AgentComposerToken(int Start, int End, char Trigger, string Prefix)
{
    public static bool TryFind(string text, int caret, out AgentComposerToken token)
    {
        token = default;
        if (caret <= 0 || caret > text.Length)
            return false;

        int start = caret - 1;
        while (start > 0 && !char.IsWhiteSpace(text[start - 1]))
            start--;
        char trigger = text[start];
        if (trigger is not ('/' or '$' or '@'))
            return false;

        int end = start + 1;
        while (end < text.Length && (IsNameCharacter(text[end]) || (trigger == '@' && text[end] is '/' or '\\')))
            end++;
        if (caret > end)
            return false;
        token = new(start, end, trigger, text[(start + 1)..caret]);
        return true;
    }

    public static IEnumerable<string> InvokedNames(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] is not ('/' or '$') || (i > 0 && !char.IsWhiteSpace(text[i - 1]))
                || !TryFind(text, i + 1, out var token) || token.End == i + 1
                || (token.End < text.Length && !char.IsWhiteSpace(text[token.End])))
                continue;
            yield return text[(i + 1)..token.End];
            i = token.End - 1;
        }
    }

    public static bool IsNameCharacter(char character) => char.IsLetterOrDigit(character) || character is '-' or '_' or '.' or ':';
}
