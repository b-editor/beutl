using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;

namespace Beutl.AgentToolkit.Sessions;

public sealed class AgentSessionManager(CreativeMemoryStore? creativeMemory = null)
{
    private readonly string _hostCompositionSeed = CreateCompositionSeed("host");
    private readonly Dictionary<string, CompositionPlanState> _compositionPlans = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _recentCompositions = new(StringComparer.Ordinal);
    private readonly List<string> _hostRecentCompositions = [];
    private readonly List<string> _preAttachPreviewedCompositions = [];
    private int _creativeDirectionRequestCount;
    private ISessionSource? _currentSource;
    private string? _compositionSessionKey;
    private string? _compositionSessionSeed;

    public IEditingSession? CurrentSession => _currentSource?.CurrentSession;

    public bool HasActiveSession => CurrentSession is not null;

    public void UseSource(ISessionSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _currentSource = source;
    }

    public IEditingSession RequireSession()
    {
        return CurrentSession
               ?? throw new SessionUnavailableException();
    }

    public string ResolveCompositionSeed(string? seed)
    {
        if (!string.IsNullOrWhiteSpace(seed))
        {
            return seed.Trim();
        }

        IEditingSession? session = CurrentSession;
        if (session is null)
        {
            return _hostCompositionSeed;
        }

        string sessionKey = GetCompositionSessionKey();
        if (!StringComparer.Ordinal.Equals(_compositionSessionKey, sessionKey))
        {
            _compositionSessionKey = sessionKey;
            _compositionSessionSeed = $"session:{sessionKey}";
        }

        return _compositionSessionSeed!;
    }

    public IReadOnlyList<string> GetRecentCompositions()
    {
        string key = GetCompositionSessionKey();
        IEnumerable<string> sessionRecent = _recentCompositions.TryGetValue(key, out List<string>? names)
            ? names
            : [];
        return sessionRecent
            .Concat(_hostRecentCompositions)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<string> GetPreAttachPreviewedCompositions()
    {
        return _preAttachPreviewedCompositions.ToArray();
    }

    public IReadOnlyList<string> GetAvoidedCompositions()
    {
        return GetRecentCompositions()
            .Concat(_preAttachPreviewedCompositions)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public int NextCreativeDirectionRequestIndex()
    {
        return Interlocked.Increment(ref _creativeDirectionRequestCount) - 1;
    }

    public IReadOnlyList<CreativeDirectionFingerprint> GetRecentCreativeFingerprints()
        => creativeMemory?.ReadRecent() ?? [];

    public void RecordCreativeDirection(CreativeDirectionFingerprint fingerprint)
        => creativeMemory?.Record(fingerprint);

    public void RecordPreAttachCompositionPreview(string name)
    {
        if (HasActiveSession || string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        AddRecent(_preAttachPreviewedCompositions, name);
    }

    public void RecordCompositionUse(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        AddRecent(_hostRecentCompositions, name);

        string key = GetCompositionSessionKey();
        if (!_recentCompositions.TryGetValue(key, out List<string>? names))
        {
            names = [];
            _recentCompositions[key] = names;
        }

        AddRecent(names, name);
    }

    private static void AddRecent(List<string> names, string name)
    {
        names.RemoveAll(item => string.Equals(item, name, StringComparison.OrdinalIgnoreCase));
        names.Insert(0, name);
        if (names.Count > 8)
        {
            names.RemoveRange(8, names.Count - 8);
        }
    }

    public CompositionPlanState StoreCompositionPlan(
        string compositionName,
        string seed,
        JsonObject inputProps,
        JsonObject desiredDocument,
        JsonArray expectedChangeSet,
        IReadOnlySet<Guid> knownNewIds)
    {
        string id = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
        var state = new CompositionPlanState(
            id,
            GetCompositionSessionKey(),
            compositionName,
            seed,
            (JsonObject)inputProps.DeepClone(),
            (JsonObject)desiredDocument.DeepClone(),
            (JsonArray)expectedChangeSet.DeepClone(),
            knownNewIds.ToArray(),
            DateTimeOffset.UtcNow);
        _compositionPlans[id] = state;
        return state;
    }

    public CompositionPlanState GetCompositionPlan(string planId)
    {
        if (!_compositionPlans.TryGetValue(planId, out CompositionPlanState? state))
        {
            throw new ReconcileException(new ToolError(
                ErrorCode.StaleHandle,
                $"Composition plan '{planId}' was not found.",
                planId,
                "Run plan_composition again and pass the returned planId to apply_composition."));
        }

        string currentKey = GetCompositionSessionKey();
        if (!StringComparer.Ordinal.Equals(state.SessionKey, currentKey))
        {
            throw new ReconcileException(new ToolError(
                ErrorCode.StaleHandle,
                $"Composition plan '{planId}' belongs to a different editing session.",
                planId,
                "Run plan_composition again in the active session."));
        }

        return state;
    }

    public void RemoveCompositionPlan(string planId)
    {
        _compositionPlans.Remove(planId);
    }

    private string GetCompositionSessionKey()
    {
        IEditingSession? session = CurrentSession;
        return session is null
            ? "host"
            : $"{session.Source}:{session.Root.Id}";
    }

    private static string CreateCompositionSeed(string scope)
    {
        return $"{scope}:{Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant()}";
    }
}

public sealed record CompositionPlanState(
    string Id,
    string SessionKey,
    string CompositionName,
    string Seed,
    JsonObject InputProps,
    JsonObject DesiredDocument,
    JsonArray ExpectedChangeSet,
    IReadOnlyList<Guid> KnownNewIds,
    DateTimeOffset CreatedAt);

public sealed class SessionUnavailableException : Exception
{
    public SessionUnavailableException()
        : base("No active editing session is available.")
    {
    }

    public ToolError ToError()
    {
        return new ToolError(
            ErrorCode.NoActiveEditorSession,
            Message,
            null,
            "Call attach_active_editor for an open editor scene, or call create_project/open_project to start a file-backed session before read_document_summary, read_document, apply_edit, render_still, or export_video.");
    }
}
