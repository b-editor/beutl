using System.Text;

namespace Beutl.AgentToolkit.Installation;

/// <summary>
/// Converts a Claude-style subagent (markdown with YAML frontmatter) into a
/// Codex agent definition (TOML with name / description /
/// developer_instructions), the format Codex loads from .codex/agents/.
/// </summary>
public static class CodexSubagentConverter
{
    public static string Convert(string markdown, string fallbackName)
    {
        (IReadOnlyDictionary<string, string> frontMatter, string body) = ParseFrontMatter(markdown);

        string name = frontMatter.GetValueOrDefault("name") is { Length: > 0 } n ? n : fallbackName;
        string description = frontMatter.GetValueOrDefault("description") ?? "";

        var builder = new StringBuilder();
        builder.Append("name = ").AppendLine(TomlBasicString(name));
        builder.Append("description = ").AppendLine(TomlBasicString(description));
        builder.AppendLine();
        builder.Append("developer_instructions = ").AppendLine(TomlMultilineString(body.Trim()));
        return builder.ToString();
    }

    private static (IReadOnlyDictionary<string, string> FrontMatter, string Body) ParseFrontMatter(string markdown)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        string[] lines = markdown.Replace("\r\n", "\n").Split('\n');
        if (lines.Length == 0 || lines[0].Trim() != "---")
        {
            return (values, markdown);
        }

        int end = Array.FindIndex(lines, 1, line => line.Trim() == "---");
        if (end < 0)
        {
            return (values, markdown);
        }

        foreach (string line in lines[1..end])
        {
            int separator = line.IndexOf(':');
            if (separator <= 0 || char.IsWhiteSpace(line[0]))
            {
                continue;
            }

            string key = line[..separator].Trim();
            string value = line[(separator + 1)..].Trim();
            if (value.Length >= 2
                && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
            {
                value = value[1..^1];
            }

            values[key] = value;
        }

        return (values, string.Join('\n', lines[(end + 1)..]));
    }

    private static string TomlBasicString(string value)
    {
        var builder = new StringBuilder("\"");
        foreach (char c in value)
        {
            switch (c)
            {
                case '\\': builder.Append("\\\\"); break;
                case '"': builder.Append("\\\""); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                default:
                    if (c < 0x20)
                    {
                        builder.Append("\\u").Append(((int)c).ToString("X4"));
                    }
                    else
                    {
                        builder.Append(c);
                    }

                    break;
            }
        }

        return builder.Append('"').ToString();
    }

    private static string TomlMultilineString(string body)
    {
        // Literal ''' blocks take no escapes, so they preserve the markdown
        // body verbatim; fall back to an escaped basic block only when the
        // body itself contains the delimiter.
        if (!body.Contains("'''"))
        {
            return "'''\n" + body + "\n'''";
        }

        string escaped = body
            .Replace("\\", "\\\\")
            .Replace("\"\"\"", "\"\"\\\"");
        return "\"\"\"\n" + escaped + "\n\"\"\"";
    }
}
