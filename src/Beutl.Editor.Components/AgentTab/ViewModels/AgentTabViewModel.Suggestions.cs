using Beutl.Agents;

namespace Beutl.Editor.Components.AgentTab.ViewModels;

internal sealed partial class AgentTabViewModel
{
    private IReadOnlyList<AgentComposerSuggestion> _providerSuggestions = [];
    private IReadOnlyList<AgentMentionCandidate> _mentionCandidates = [];
    private readonly Dictionary<string, AgentContextMention> _acceptedMentions = new(StringComparer.OrdinalIgnoreCase);
    private int _suggestionsVersion;
    private bool _suggestionsRequested;
    private int _mentionVersion;
    private bool _mentionsRequested;

    internal event Action? ComposerSuggestionsChanged;

    internal AgentComposerSuggestionRow[] GetComposerSuggestions(AgentComposerToken token)
    {
        if (token.Trigger == '@')
            return GetMentionSuggestions(token.Prefix);

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AgentComposerSuggestion[] builtIn = token.Trigger == '/'
            ? [new("compact", AgentStrings.CompactContext, AgentComposerSuggestionKind.Command),
                new("clear", AgentStrings.ClearContext, AgentComposerSuggestionKind.Command)]
            : [];
        return builtIn.Concat(_host.BundledSkills).Concat(_providerSuggestions)
            .Where(suggestion => !string.IsNullOrWhiteSpace(suggestion.Name)
                && (token.Trigger == '/' || suggestion.Kind == AgentComposerSuggestionKind.Skill))
            .Select(suggestion => (Suggestion: suggestion, Name: NormalizeSuggestionName(suggestion.Name)))
            .Where(item => item.Name.Length > 0 && item.Name.All(AgentComposerToken.IsNameCharacter)
                && item.Name.StartsWith(token.Prefix, StringComparison.OrdinalIgnoreCase) && names.Add(item.Name))
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(item => new AgentComposerSuggestionRow(token.Trigger + item.Name, item.Suggestion)).ToArray();
    }

    private AgentComposerSuggestionRow[] GetMentionSuggestions(string prefix)
    {
        prefix = prefix.Replace('\\', '/');
        var matches = _mentionCandidates
            .Select(candidate => new AgentComposerSuggestionRow(MentionToken(candidate.Mention), Candidate: candidate))
            .Where(row => row.Title.Contains(prefix, StringComparison.OrdinalIgnoreCase)
                || row.Token.Contains(prefix, StringComparison.OrdinalIgnoreCase)
                || (row.Candidate!.Mention.Kind == AgentContextMentionKind.File
                    && row.Candidate.Detail.Contains(prefix, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(row => row.Candidate!.Mention.Kind)
            .ThenBy(row => row.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.Detail, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (prefix.Length > 0)
            return matches;

        // Show a match from each kind in the first few rows of the unfiltered popup.
        var byKind = matches.GroupBy(row => row.Candidate!.Mention.Kind).ToArray();
        return [.. byKind.Select(group => group.First()), .. byKind.SelectMany(group => group.Skip(1))];
    }

    private static string MentionToken(AgentContextMention mention)
    {
        string name = string.Concat(mention.Name.Select(character =>
            AgentComposerToken.IsNameCharacter(character) ? character : '-')).Trim('-');
        return "@" + (name.Length == 0 ? mention.Kind.ToString().ToLowerInvariant() : name);
    }

    internal string AcceptComposerSuggestion(AgentComposerSuggestionRow row)
    {
        if (row.Candidate is not { Mention: var mention })
            return row.Token;

        string token = RegisterMention(mention, row.Token);
        MarkDraftChanged();
        return token;
    }

    private string RegisterMention(AgentContextMention mention, string baseToken)
    {
        string token = baseToken;
        int suffix = 2;
        while (_acceptedMentions.TryGetValue(token, out var existing)
            && (existing.Kind != mention.Kind || existing.TargetId != mention.TargetId || existing.Path != mention.Path
                || existing.SceneId != mention.SceneId || existing.ElementId != mention.ElementId))
        {
            token = FormattableString.Invariant($"{baseToken}-{suffix++}");
        }
        _acceptedMentions[token] = mention with { Token = token };
        return token;
    }

    private IReadOnlyList<AgentContextMention>? GetAcceptedMentions(string? prompt)
    {
        if (string.IsNullOrEmpty(prompt) || _acceptedMentions.Count == 0)
            return null;

        var mentions = new HashSet<AgentContextMention>();
        var ordered = new List<AgentContextMention>();
        for (int index = 0; index < prompt.Length; index++)
        {
            if (prompt[index] != '@' || (index > 0 && !char.IsWhiteSpace(prompt[index - 1]))
                || !AgentComposerToken.TryFind(prompt, index + 1, out var token))
                continue;
            string name = prompt[index..token.End];
            if ((_acceptedMentions.TryGetValue(name, out var mention)
                 || _acceptedMentions.TryGetValue(name.TrimEnd('.', ':'), out mention))
                && mentions.Add(mention))
            {
                ordered.Add(mention);
            }
            index = token.End - 1;
        }
        return ordered.Count == 0 ? null : ordered;
    }

    private void RestoreAcceptedMentions(IReadOnlyList<AgentContextMention>? mentions)
    {
        _acceptedMentions.Clear();
        foreach (var mention in mentions ?? [])
        {
            if (mention.Token is { Length: > 0 } token)
                _acceptedMentions[token] = mention;
        }
    }

    private string[] GetInvokedProviderSkills(string prompt)
    {
        var names = AgentComposerToken.InvokedNames(prompt).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return _providerSuggestions.Where(suggestion => suggestion.Kind == AgentComposerSuggestionKind.Skill
                && !string.IsNullOrWhiteSpace(suggestion.Name))
            .Select(suggestion => NormalizeSuggestionName(suggestion.Name)).Where(names.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string NormalizeSuggestionName(string name) => name.Trim().TrimStart('/', '$');

    internal void DiscoverComposerSuggestions(char trigger)
    {
        if (trigger == '@')
        {
            if (!_mentionsRequested)
            {
                _mentionsRequested = true;
                _ = LoadMentionCandidatesAsync(_mentionVersion);
            }
            return;
        }

        ResetMentionCandidates();
        if (_suggestionsRequested || SelectedProvider is not { IsAvailable: true } provider)
            return;

        _suggestionsRequested = true;
        _ = LoadComposerSuggestionsAsync(provider.Descriptor, _selectedThread?.Id, _suggestionsVersion);
    }

    private async Task LoadMentionCandidatesAsync(int version)
    {
        var ct = _lifetime.Token;
        try
        {
            var candidates = await _host.GetMentionCandidatesAsync(_editor, _projectPath, ct);
            if (!_disposed && version == _mentionVersion)
            {
                _mentionCandidates = candidates;
                ComposerSuggestionsChanged?.Invoke();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (!_disposed && version == _mentionVersion)
                ReportError(ex);
        }
    }

    internal void ResetMentionCandidates()
    {
        if (!_mentionsRequested)
            return;
        _mentionVersion++;
        _mentionsRequested = false;
        _mentionCandidates = [];
    }

    private async Task LoadComposerSuggestionsAsync(AgentProviderDescriptor provider, Guid? threadId, int version)
    {
        var ct = _lifetime.Token;
        try
        {
            var suggestions = await _host.Runtime.ListComposerSuggestionsAsync(_projectPath, provider, threadId, ct);
            if (version == _suggestionsVersion && !_disposed)
            {
                _providerSuggestions = suggestions;
                ComposerSuggestionsChanged?.Invoke();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }

    private void OnComposerSuggestionsChanged(AgentComposerSuggestionsChanged update) => Post(() =>
    {
        if (update.ThreadId != _selectedThread?.Id)
            return;

        _suggestionsVersion++;
        _suggestionsRequested = true;
        _providerSuggestions = update.Suggestions;
        ComposerSuggestionsChanged?.Invoke();
    });

    private void ResetComposerSuggestions()
    {
        ResetMentionCandidates();
        _suggestionsVersion++;
        _suggestionsRequested = false;
        _providerSuggestions = [];
        ComposerSuggestionsChanged?.Invoke();
    }
}

internal sealed record AgentComposerSuggestionRow(
    string Token, AgentComposerSuggestion? Suggestion = null, AgentMentionCandidate? Candidate = null)
{
    public string Title => Candidate is { } candidate ? "@" + candidate.Mention.Name : Token;
    public string? ArgumentHint => Suggestion?.ArgumentHint;
    public string? Detail => Candidate?.Detail ?? Suggestion?.Description;
    public string KindLabel => Candidate?.Mention.Kind switch
    {
        AgentContextMentionKind.Scene => AgentStrings.MentionScene,
        AgentContextMentionKind.Element => AgentStrings.MentionElement,
        AgentContextMentionKind.File => AgentStrings.MentionFile,
        AgentContextMentionKind.Object => AgentStrings.MentionObject,
        _ => Suggestion?.Kind == AgentComposerSuggestionKind.Skill ? AgentStrings.SuggestionSkill : AgentStrings.SuggestionCommand
    };
}
