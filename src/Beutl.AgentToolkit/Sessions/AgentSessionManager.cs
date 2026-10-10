using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;

namespace Beutl.AgentToolkit.Sessions;

public sealed class CompositionPlanStore
{
    internal object SyncRoot { get; } = new();

    internal string HostSeed { get; } = $"host:{Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant()}";

    internal Dictionary<string, CompositionPlanState> Plans { get; } = new(StringComparer.Ordinal);
}

public sealed class AgentSessionManager(CompositionPlanStore? plans = null)
{
    private readonly CompositionPlanStore _plans = plans ?? new();
    private volatile ISessionSource? _currentSource;
    private Func<IEditingSession>? _requestTarget;
    private IEditingSession? _requestSession;

    public IEditingSession? CurrentSession => _requestTarget is { } resolve
        ? _requestSession ??= resolve()
        : _currentSource?.CurrentSession;

    // Live MCP registers this manager per request. Resolve lazily, after the MCP argument filter,
    // and capture one binding so later UI navigation cannot retarget this call.
    public void UseRequestTarget(Func<IEditingSession> resolve)
    {
        ArgumentNullException.ThrowIfNull(resolve);
        _requestTarget = resolve;
        _requestSession = null;
    }

    public string CurrentSessionKey => GetCompositionSessionKey();

    // Key a SPECIFIC session rather than re-reading the current one, so callers holding a session
    // reference are immune to a concurrent session switch between two reads.
    public string GetSessionKey(IEditingSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return BuildSessionKey(session);
    }

    // Discriminate by the project URI, not just Root.Id: save-as/copy preserves the persisted
    // Scene.Id, so two distinct file sessions can share Source:Root.Id and cross-contaminate cached
    // plans. The URI is stable across a live session's volatile SessionId for
    // the same document; it falls back to Root.Id for an in-memory scene that has no URI yet.
    private static string BuildSessionKey(IEditingSession session)
    {
        return session.ReadOnSession(() =>
            $"{session.Source}:{session.Root.Uri?.ToString() ?? session.Root.Id.ToString()}");
    }

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
            return _plans.HostSeed;
        }

        return $"session:{BuildSessionKey(session)}";
    }

    // sessionKey is the key of the session the plan was BUILT against (GetSessionKey on the
    // captured session), not re-read here: a session switch between plan and store must not file
    // the plan under the new session's key.
    public CompositionPlanState StoreCompositionPlan(
        string sessionKey,
        string compositionName,
        string seed,
        JsonObject inputProps,
        JsonObject desiredDocument,
        JsonArray expectedChangeSet,
        IReadOnlySet<Guid> knownNewIds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionKey);
        string id = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
        var state = new CompositionPlanState(
            id,
            sessionKey,
            compositionName,
            seed,
            (JsonObject)inputProps.DeepClone(),
            (JsonObject)desiredDocument.DeepClone(),
            (JsonArray)expectedChangeSet.DeepClone(),
            knownNewIds.ToArray(),
            DateTimeOffset.UtcNow);
        lock (_plans.SyncRoot)
        {
            // Plans are only removed on a successful apply; abandoned ones (never applied, failed
            // validation, or from a swapped-away session) would otherwise accumulate for the
            // lifetime of the in-app host, each holding a full cloned document.
            while (_plans.Plans.Count >= MaxRetainedCompositionPlans)
            {
                string oldest = _plans.Plans.Values.MinBy(plan => plan.CreatedAt)!.Id;
                _plans.Plans.Remove(oldest);
            }

            _plans.Plans[id] = state;
        }

        return state;
    }

    private const int MaxRetainedCompositionPlans = 32;

    // sessionKey is the key of the session the caller captured and will mutate; validating against
    // it (not a key re-read now) stops a plan being accepted for a session swapped in mid-call and
    // then applied to the earlier captured scene.
    public CompositionPlanState GetCompositionPlan(string planId, string sessionKey)
    {
        string currentKey = sessionKey;
        CompositionPlanState? state;
        lock (_plans.SyncRoot)
        {
            _plans.Plans.TryGetValue(planId, out state);
        }

        if (state is null)
        {
            throw new ReconcileException(new ToolError(
                ErrorCode.StaleHandle,
                $"Composition plan '{planId}' was not found.",
                planId,
                "Run plan_composition again and pass the returned planId to apply_composition."));
        }

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
        lock (_plans.SyncRoot)
        {
            _plans.Plans.Remove(planId);
        }
    }

    private string GetCompositionSessionKey()
    {
        IEditingSession? session = CurrentSession;
        return session is null
            ? "host"
            : BuildSessionKey(session);
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
            "If the editor is exporting or switching projects, wait until it is enabled and retry. In live MCP, call list_scenes and pass sceneId on each scene tool call. In stdio MCP, call create_project/open_project to start a file-backed session.");
    }
}
