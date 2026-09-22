using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Beutl.Editor.Components.WebBrowserTab;

// Only translate rules whose restrictions can be preserved by WebKit. Unknown options must
// never be dropped: doing so turns a narrow filter (or exception) into a much broader one.
internal sealed class BrowserAdBlockRules
{
    internal const int MaximumRules = 100_000;
    private static readonly string[] ResourceTypes = ["document", "image", "style-sheet", "script", "font", "media", "raw", "svg-document", "popup"];
    private readonly Dictionary<string, List<NetworkRule>> _index = new(StringComparer.Ordinal);
    private readonly List<NetworkRule> _unindexed = [];
    private readonly List<NetworkRule> _network = [];
    private readonly List<CosmeticRule> _cosmetic = [];
    private ILookup<string, CosmeticRule> _cosmeticExceptions = Array.Empty<CosmeticRule>().ToLookup(r => r.Selector);
    internal int SupportedCount => _network.Count + _cosmetic.Count;
    internal int UnsupportedCount { get; private set; }

    internal static BrowserAdBlockRules Parse(string text)
    {
        var result = new BrowserAdBlockRules();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } raw)
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('!') || line.StartsWith("[Adblock", StringComparison.OrdinalIgnoreCase)
                || !seen.Add(line)) continue;
            if (result.SupportedCount + result.UnsupportedCount >= MaximumRules)
                throw new InvalidDataException("The filter lists contain too many rules.");
            if (line.Length > 4096 || !result.TryAdd(line)) result.UnsupportedCount++;
        }
        result._cosmeticExceptions = result._cosmetic.Where(r => r.Exception).ToLookup(r => r.Selector);
        return result;
    }

    private bool TryAdd(string line)
    {
        int cosmetic = line.IndexOf("#@#", StringComparison.Ordinal);
        bool cosmeticException = cosmetic >= 0;
        if (!cosmeticException) cosmetic = line.IndexOf("##", StringComparison.Ordinal);
        if (cosmetic >= 0)
        {
            string selector = line[(cosmetic + (cosmeticException ? 3 : 2))..];
            if (selector.Length == 0 || selector.Contains('{') || selector.Contains('}')
                || selector.Contains(":-abp-", StringComparison.Ordinal) || selector.Contains(":style(", StringComparison.Ordinal)
                || selector.Contains(":remove(", StringComparison.Ordinal) || selector.Contains("+js(", StringComparison.Ordinal)
                || selector.Contains(":has-text(", StringComparison.Ordinal) || selector.Contains(":matches-", StringComparison.Ordinal)) return false;
            if (!TryDomains(line[..cosmetic], ',', out string[] include, out string[] exclude)) return false;
            // Negated cosmetic exceptions require a complement of domain sets, which WebKit
            // cannot represent as a single trigger. Keep the unsupported rule visible in the count.
            if (cosmeticException && exclude.Length != 0) return false;
            _cosmetic.Add(new(selector, include, exclude, cosmeticException));
            return true;
        }
        if (line.Contains('#')) return false;

        bool exception = line.StartsWith("@@", StringComparison.Ordinal);
        string pattern = exception ? line[2..] : line;
        var types = new HashSet<string>(StringComparer.Ordinal);
        var excludedTypes = new HashSet<string>(StringComparer.Ordinal);
        string[] includedDomains = [], excludedDomains = [];
        bool? thirdParty = null;
        bool matchCase = false;
        bool childFrame = false;
        int optionStart = pattern.LastIndexOf('$');
        if (optionStart >= 0)
        {
            foreach (string option in pattern[(optionStart + 1)..].Split(','))
            {
                bool negated = option.StartsWith('~');
                string name = negated ? option[1..] : option;
                if (name is "third-party" or "3p") thirdParty = !negated;
                else if (name == "match-case" && !negated) matchCase = true;
                else if (name.StartsWith("domain=", StringComparison.Ordinal) && !negated)
                {
                    if (!TryDomains(name[7..], '|', out includedDomains, out excludedDomains)) return false;
                }
                else
                {
                    string? type = name switch
                    {
                        "image" => "image",
                        "stylesheet" => "style-sheet",
                        "script" => "script",
                        "font" => "font",
                        "media" => "media",
                        "xmlhttprequest" => "raw",
                        "subdocument" => "document",
                        _ => null
                    };
                    if (type == null || (name == "subdocument" && negated)) return false;
                    (negated ? excludedTypes : types).Add(type);
                    childFrame |= name == "subdocument";
                }
            }
            pattern = pattern[..optionStart];
        }
        // A child-frame constraint applies to the whole native trigger, not just document resources.
        if (childFrame && (types.Count != 1 || excludedTypes.Count != 0)) return false;
        if (pattern.Length == 0 || pattern.Any(c => c > 127) || (pattern.StartsWith('/') && pattern.EndsWith('/'))) return false;
        if (types.Count == 0) types.UnionWith(ResourceTypes);
        types.ExceptWith(excludedTypes);
        if (types.Count == 0) return false;

        string regex = ToRegex(pattern);
        var rule = new NetworkRule(regex, types.ToArray(), includedDomains, excludedDomains, thirdParty, matchCase, childFrame, exception);
        _network.Add(rule);
        // Index on a literal substring; unlike splitting a request into words, this also finds
        // filters embedded in a path or hostname. The remaining regex work is bounded per bucket.
        string? key = Regex.Matches(pattern.ToLowerInvariant(), "[a-z0-9%]{5,}")
            .Select(m => m.Value).MaxBy(s => s.Length)?[..5];
        if (key == null) _unindexed.Add(rule);
        else
        {
            if (!_index.TryGetValue(key, out var bucket)) _index.Add(key, bucket = []);
            bucket.Add(rule);
        }
        return true;
    }

    private static bool TryDomains(string text, char separator, out string[] include, out string[] exclude)
    {
        var allowed = new List<string>();
        var denied = new List<string>();
        foreach (string entry in text.Split(separator, StringSplitOptions.RemoveEmptyEntries))
        {
            bool negative = entry.StartsWith('~');
            string domain = (negative ? entry[1..] : entry).ToLowerInvariant();
            if (Uri.CheckHostName(domain) != UriHostNameType.Dns || domain.Contains('*'))
            {
                include = exclude = [];
                return false;
            }
            (negative ? denied : allowed).Add(domain);
        }
        include = allowed.ToArray();
        exclude = denied.ToArray();
        return include.Length == 0 || exclude.Length == 0;
    }

    internal static string ToRegex(string pattern)
    {
        var result = new StringBuilder();
        if (pattern.StartsWith("||", StringComparison.Ordinal))
        {
            result.Append("^https?://([^/?#]*\\.)?");
            pattern = pattern[2..];
        }
        else if (pattern.StartsWith('|')) { result.Append('^'); pattern = pattern[1..]; }
        bool end = pattern.EndsWith('|');
        if (end) pattern = pattern[..^1];
        foreach (char c in pattern)
        {
            if (c == '*') result.Append(".*");
            else if (c == '^') result.Append("([^a-zA-Z0-9_%.\\-]|$)");
            else
            {
                if (".\\+?()[]{}$|".Contains(c)) result.Append('\\');
                result.Append(c);
            }
        }
        if (end) result.Append('$');
        return result.ToString();
    }

    internal string ToWebKitJson()
    {
        var rules = new List<object>();
        foreach (CosmeticRule rule in _cosmetic.Where(r => !r.Exception))
        {
            CosmeticRule[] exceptions = _cosmeticExceptions[rule.Selector].ToArray();
            if (exceptions.Any(r => r.Include.Length == 0)) continue;
            // Combining include and exclude domains is not supported by WebKit. Skip that
            // cosmetic filter rather than hiding content on a site with an explicit exception.
            if (rule.Include.Length != 0 && exceptions.Length != 0) continue;
            var trigger = CreateTrigger(".*", rule.Include, rule.Exclude.Concat(exceptions.SelectMany(r => r.Include)).Distinct().ToArray());
            rules.Add(new { trigger, action = new { type = "css-display-none", selector = rule.Selector } });
        }
        foreach (NetworkRule rule in _network.OrderBy(r => r.Exception))
        {
            foreach (string pattern in ToWebKitPatterns(rule.Pattern))
            {
                var trigger = CreateTrigger(pattern, rule.Include, rule.Exclude);
                trigger["resource-type"] = rule.Types;
                trigger["url-filter-is-case-sensitive"] = rule.MatchCase;
                if (rule.ThirdParty is { } thirdParty) trigger["load-type"] = new[] { thirdParty ? "third-party" : "first-party" };
                if (rule.ChildFrame) trigger["load-context"] = new[] { "child-frame" };
                rules.Add(new { trigger, action = new { type = rule.Exception ? "ignore-previous-rules" : "block" } });
            }
        }
        return JsonSerializer.Serialize(rules);
    }

    internal static IEnumerable<string> ToWebKitPatterns(string pattern)
    {
        const string separator = "([^a-zA-Z0-9_%.\\-]|$)";
        const string boundary = "[^a-zA-Z0-9_%.\\-]";
        // WebKit rejects disjunctions. ABP's '^' is either a separator character or
        // end-of-URL; the latter can only match if the rest of the filter is empty.
        yield return pattern.Replace(separator, boundary, StringComparison.Ordinal);
        for (int index = pattern.IndexOf(separator, StringComparison.Ordinal); index >= 0;
             index = pattern.IndexOf(separator, index + separator.Length, StringComparison.Ordinal))
        {
            string remainder = pattern[(index + separator.Length)..].Replace(separator, "", StringComparison.Ordinal)
                .Replace(".*", "", StringComparison.Ordinal).TrimEnd('$');
            if (remainder.Length == 0)
                yield return pattern[..index].Replace(separator, boundary, StringComparison.Ordinal) + "$";
        }
    }

    private static Dictionary<string, object> CreateTrigger(string pattern, string[] include, string[] exclude)
    {
        var result = new Dictionary<string, object> { ["url-filter"] = pattern };
        // WebKit's leading '*' matches the domain itself and its subdomains.
        if (include.Length != 0) result["if-domain"] = include.Select(d => "*" + d).ToArray();
        if (exclude.Length != 0) result["unless-domain"] = exclude.Select(d => "*" + d).ToArray();
        return result;
    }

    internal bool ShouldBlock(Uri request, Uri? page, string resourceType, bool isChildFrame = false, bool? isThirdParty = null)
    {
        if (!BrowserMediaDownload.IsHttpUri(request)) return false;
        string url = request.AbsoluteUri;
        string normalized = url.ToLowerInvariant();
        var candidates = new HashSet<NetworkRule>(_unindexed, ReferenceEqualityComparer.Instance);
        for (int i = 0; i <= normalized.Length - 5; i++)
            if (_index.TryGetValue(normalized.Substring(i, 5), out var bucket)) candidates.UnionWith(bucket);
        bool blocked = false;
        foreach (NetworkRule rule in candidates)
        {
            if (!rule.Types.Contains(resourceType) || (rule.ChildFrame && !isChildFrame)
                || !MatchesDomains(page?.Host ?? "", rule.Include, rule.Exclude)) continue;
            if (rule.ThirdParty is { } thirdParty)
            {
                if (page == null) continue;
                bool? actual = isThirdParty ?? InferThirdParty(request, page);
                if (actual == null || thirdParty != actual) continue;
            }
            if (!rule.Matches(url)) continue;
            if (rule.Exception) return false;
            blocked = true;
        }
        return blocked;
    }

    private static bool? InferThirdParty(Uri request, Uri page)
    {
        if (request.IdnHost == page.IdnHost) return false;
        string[] target = request.IdnHost.Split('.'), origin = page.IdnHost.Split('.');
        // When Fetch Metadata is absent, shared suffixes need a public-suffix database.
        // Do not guess: for example sibling hosts on example.co.jp are the same site,
        // while sibling github.io tenants are not. Skip party-constrained rules here.
        if (target.Length >= 2 && origin.Length >= 2 && target[^1] == origin[^1] && target[^2] == origin[^2]) return null;
        return true;
    }

    internal string GetCosmeticScript(Uri page, bool enabled)
    {
        string[] selectors = enabled ? _cosmetic.Where(r => !r.Exception && MatchesDomains(page.Host, r.Include, r.Exclude)
            && !_cosmeticExceptions[r.Selector].Any(e => MatchesDomains(page.Host, e.Include, e.Exclude)))
            .Select(r => r.Selector).Distinct().ToArray() : [];
        // JSON encoding keeps downloaded filter text out of executable JavaScript. insertRule
        // rejects invalid/unsupported CSS one selector at a time, without discarding the list.
        return "(()=>{const id='beutl-adblock-style';document.getElementById(id)?.remove();const s=document.createElement('style');s.id=id;(document.head||document.documentElement).append(s);for(const selector of "
            + JsonSerializer.Serialize(selectors) + "){try{s.sheet.insertRule(selector+'{display:none!important}');}catch{}}})()";
    }

    private static bool MatchesDomains(string host, string[] include, string[] exclude) =>
        (include.Length == 0 || include.Any(d => DomainMatches(host, d))) && !exclude.Any(d => DomainMatches(host, d));

    internal static bool DomainMatches(string host, string domain) => host.Equals(domain, StringComparison.OrdinalIgnoreCase)
        || host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase);

    private sealed record CosmeticRule(string Selector, string[] Include, string[] Exclude, bool Exception);
    private sealed record NetworkRule(string Pattern, string[] Types, string[] Include, string[] Exclude,
        bool? ThirdParty, bool MatchCase, bool ChildFrame, bool Exception)
    {
        private Regex? _regex;
        internal bool Matches(string url)
        {
            _regex ??= new Regex(Pattern, RegexOptions.CultureInvariant | (MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase), TimeSpan.FromMilliseconds(20));
            try { return _regex.IsMatch(url); }
            catch (RegexMatchTimeoutException) { return false; }
        }
    }
}
