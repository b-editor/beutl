using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;

namespace Beutl.AgentToolkit.Sessions;

public sealed class AgentSessionManager
{
    private readonly string _hostCompositionSeed = CreateCompositionSeed("host");
    private readonly Dictionary<string, CompositionPlanState> _compositionPlans = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _recentCompositions = new(StringComparer.Ordinal);
    private ISessionSource? _currentSource;
    private string? _compositionSessionKey;
    private string? _compositionSessionSeed;

    public IEditingSession? CurrentSession => _currentSource?.CurrentSession;

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

        string sessionKey = $"{session.Source}:{session.SessionId}";
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
        return _recentCompositions.TryGetValue(key, out List<string>? names)
            ? names.ToArray()
            : [];
    }

    public void RecordCompositionUse(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        string key = GetCompositionSessionKey();
        if (!_recentCompositions.TryGetValue(key, out List<string>? names))
        {
            names = [];
            _recentCompositions[key] = names;
        }

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
        JsonArray expectedChangeSet)
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
            : $"{session.Source}:{session.SessionId}";
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
            "In the in-app host, call attach_active_editor before read_document_summary, read_document, plan_edit, apply_edit, render_still, or export_video. In the stdio host, call open_project or create_project first.");
    }
}
