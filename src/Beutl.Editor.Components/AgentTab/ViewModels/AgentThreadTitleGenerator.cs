using System.Resources;
using System.Text.RegularExpressions;

namespace Beutl.Editor.Components.AgentTab.ViewModels;

internal static partial class AgentThreadTitleGenerator
{
    private static readonly HashSet<string> s_placeholderTitles = CreatePlaceholderTitles();

    public static bool IsPlaceholderTitle(string text)
        => string.IsNullOrWhiteSpace(text)
            || text == AgentStrings.PastedText
            || string.Equals(text, AgentStrings.NewThread, StringComparison.OrdinalIgnoreCase)
            || s_placeholderTitles.Contains(text);

    public static string ForkTitle(string title)
        => string.Format(CultureInfo.CurrentCulture, AgentStrings.ForkedThreadTitle, title);

    public static string? MakeTitle(string text)
    {
        bool inFence = false;
        foreach (string raw in text.ReplaceLineEndings("\n").Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith("```", StringComparison.Ordinal) || line.StartsWith("~~~", StringComparison.Ordinal))
            {
                inFence = !inFence;
                continue;
            }
            if (inFence || line.StartsWith('/') || line.StartsWith('$') || line.StartsWith('@') && !line.Contains(' '))
                continue;
            line = LeadingMarkdownPrefixRegex().Replace(line, "");
            line = MarkdownLinkRegex().Replace(line, "$1");
            line = InlineReferenceRegex().Replace(line, "");
            line = WhitespaceRegex().Replace(line.Replace("`", "").Replace("**", "").Replace("__", "").Replace("~~", ""), " ")
                .Trim(' ', '*', '_', '-', '.', '!', '?', '。', '！', '？');
            if (IsPlaceholderTitle(line) || GreetingTitleRegex().IsMatch(line))
                continue;
            int[] characters = StringInfo.ParseCombiningCharacters(line);
            return characters.Length > 60 ? line[..characters[59]].TrimEnd() + "…" : line;
        }
        return null;
    }

    private static HashSet<string> CreatePlaceholderTitles()
    {
        var resources = new ResourceManager("Beutl.Language.AgentStrings", typeof(AgentStrings).Assembly);
        var titles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string locale in new[] { "", "ja", "es", "ko-KR", "zh-CN" })
        {
            if (resources.GetString(nameof(AgentStrings.NewThread), CultureInfo.GetCultureInfo(locale)) is { Length: > 0 } value)
                titles.Add(value);
        }
        return titles;
    }

    [GeneratedRegex(@"^\s*(?:#{1,6}\s+|>\s*|[-+*]\s+|\d+[.)]\s+)+")]
    private static partial Regex LeadingMarkdownPrefixRegex();

    [GeneratedRegex(@"!?\[([^\]]*)\]\([^\r\n)]*\)")]
    private static partial Regex MarkdownLinkRegex();

    [GeneratedRegex(@"(?<!\S)(?:https?://\S+|[$@][\p{L}\p{N}_.:-]+)(?=\s|$)")]
    private static partial Regex InlineReferenceRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"^(?:hi|hello|hey|test|hola|prueba|こんにちは|テスト|안녕하세요|你好|测试)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GreetingTitleRegex();
}
