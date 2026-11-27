namespace Beutl.Editor.Components.AgentTab;

internal enum AgentMarkdownLinkKind { Time, Element, Object, Scene, LocalFile }

internal sealed record AgentMarkdownLink(
    AgentMarkdownLinkKind Kind,
    Guid? TargetId = null,
    Guid? SceneId = null,
    Guid? ElementId = null,
    MediaTime? Time = null,
    string? PropertyName = null,
    string? LocalPath = null)
{
    internal static AgentMarkdownLink? Parse(Uri? uri)
    {
        if (uri is null)
            return null;
        if (!uri.IsAbsoluteUri || uri.Scheme != "beutl")
            return AgentMarkdownLocalPath.Decode(uri.OriginalString) is { } path ? new(AgentMarkdownLinkKind.LocalFile, LocalPath: path) : null;
        if (uri.UserInfo.Length != 0 || !uri.IsDefaultPort || uri.Fragment.Length != 0 || uri.AbsolutePath.Length < 2
            || ParseQuery(uri.Query) is not { } query)
            return null;

        string value = Uri.UnescapeDataString(uri.AbsolutePath[1..]);
        if (!ReadId(query, "scene", out Guid? scene) || !ReadId(query, "element", out Guid? element))
            return null;
        MediaTime? time = null;
        if (query.Remove("time", out string? timeValue))
        {
            if (!TryParseTime(timeValue, out var parsed)) return null;
            time = parsed;
        }
        query.Remove("property", out string? property);
        if (query.Count != 0 || property?.Length == 0)
            return null;

        if (uri.Host == "time")
            return time is null && element is null && property is null && TryParseTime(value, out var seek)
                ? new(AgentMarkdownLinkKind.Time, SceneId: scene, Time: seek) : null;
        if (!Guid.TryParseExact(value, "D", out Guid target))
            return null;

        return uri.Host switch
        {
            "element" when element is null && property is null => new(AgentMarkdownLinkKind.Element, target, scene, Time: time),
            "object" when time is null => new(AgentMarkdownLinkKind.Object, target, scene, element, PropertyName: property),
            "scene" when scene is null && element is null && property is null => new(AgentMarkdownLinkKind.Scene, SceneId: target, Time: time),
            _ => null
        };
    }

    private static Dictionary<string, string>? ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (query.Length > 0)
        {
            foreach (string pair in query[1..].Split('&'))
            {
                int separator = pair.IndexOf('=');
                if (separator <= 0 || !result.TryAdd(Uri.UnescapeDataString(pair[..separator]), Uri.UnescapeDataString(pair[(separator + 1)..])))
                    return null;
            }
        }
        return result;
    }

    private static bool ReadId(Dictionary<string, string> query, string key, out Guid? id)
    {
        id = null;
        if (!query.Remove(key, out string? value))
            return true;
        if (!Guid.TryParseExact(value, "D", out Guid parsed))
            return false;
        id = parsed;
        return true;
    }

    private static bool TryParseTime(string value, out MediaTime time)
    {
        if (value.Contains(':'))
            return GotoTimecodeParser.TryParseAbsolute(value, out time, out _);

        time = default;
        if (value.EndsWith('s') || value.EndsWith('S'))
            value = value[..^1];
        if (!double.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out double seconds) || !double.IsFinite(seconds))
            return false;

        try
        {
            time = MediaTime.FromSeconds(seconds);
        }
        catch (OverflowException)
        {
            time = seconds < 0 ? MediaTime.MinValue : MediaTime.MaxValue;
        }
        return true;
    }
}
