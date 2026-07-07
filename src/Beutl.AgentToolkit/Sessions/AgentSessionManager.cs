using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Rendering;

namespace Beutl.AgentToolkit.Sessions;

public sealed class AgentSessionManager(CreativeMemoryStore? creativeMemory = null)
{
    // GetCompositionSessionKey calls ReadOnSession; resolve it OUTSIDE the lock or an
    // editor-thread caller waiting on the lock can deadlock.
    private readonly object _stateLock = new();
    private readonly string _hostCompositionSeed = CreateCompositionSeed("host");
    private readonly Dictionary<string, CompositionPlanState> _compositionPlans = new(StringComparer.Ordinal);
    private readonly Dictionary<string, QualityReviewBaseline> _qualityReviewBaselines = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _recentCompositions = new(StringComparer.Ordinal);
    private readonly List<string> _hostRecentCompositions = [];
    private readonly List<string> _preAttachPreviewedCompositions = [];
    private int _creativeDirectionRequestCount;
    private volatile ISessionSource? _currentSource;
    private string? _compositionSessionKey;
    private string? _compositionSessionSeed;

    public IEditingSession? CurrentSession => _currentSource?.CurrentSession;

    public bool HasActiveSession => CurrentSession is not null;

    public string CurrentSessionKey => GetCompositionSessionKey();

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
        lock (_stateLock)
        {
            if (!StringComparer.Ordinal.Equals(_compositionSessionKey, sessionKey))
            {
                _compositionSessionKey = sessionKey;
                _compositionSessionSeed = $"session:{sessionKey}";
            }

            return _compositionSessionSeed!;
        }
    }

    public IReadOnlyList<string> GetRecentCompositions()
    {
        string key = GetCompositionSessionKey();
        List<string>? sessionRecentSnapshot;
        List<string> hostRecentSnapshot;
        lock (_stateLock)
        {
            sessionRecentSnapshot = _recentCompositions.TryGetValue(key, out List<string>? names)
                ? names.ToList()
                : null;
            hostRecentSnapshot = _hostRecentCompositions.ToList();
        }

        return (sessionRecentSnapshot ?? [])
            .Concat(hostRecentSnapshot)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<string> GetPreAttachPreviewedCompositions()
    {
        lock (_stateLock)
        {
            return _preAttachPreviewedCompositions.ToArray();
        }
    }

    public IReadOnlyList<string> GetAvoidedCompositions()
    {
        return GetRecentCompositions()
            .Concat(GetPreAttachPreviewedCompositions())
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

        lock (_stateLock)
        {
            AddRecent(_preAttachPreviewedCompositions, name);
        }
    }

    public void RecordCompositionUse(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        string key = GetCompositionSessionKey();
        lock (_stateLock)
        {
            AddRecent(_hostRecentCompositions, name);

            if (!_recentCompositions.TryGetValue(key, out List<string>? names))
            {
                names = [];
                _recentCompositions[key] = names;
            }

            AddRecent(names, name);
        }
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
        string sessionKey = GetCompositionSessionKey();
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
        lock (_stateLock)
        {
            // Plans are only removed on a successful apply; abandoned ones (never applied, failed
            // validation, or from a swapped-away session) would otherwise accumulate for the
            // lifetime of the in-app host, each holding a full cloned document.
            while (_compositionPlans.Count >= MaxRetainedCompositionPlans)
            {
                string oldest = _compositionPlans.Values.MinBy(plan => plan.CreatedAt)!.Id;
                _compositionPlans.Remove(oldest);
            }

            _compositionPlans[id] = state;
        }

        return state;
    }

    private const int MaxRetainedCompositionPlans = 32;

    public CompositionPlanState GetCompositionPlan(string planId)
    {
        string currentKey = GetCompositionSessionKey();
        CompositionPlanState? state;
        lock (_stateLock)
        {
            _compositionPlans.TryGetValue(planId, out state);
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
        lock (_stateLock)
        {
            _compositionPlans.Remove(planId);
        }
    }

    public void StoreQualityReviewBaseline(QualityReviewBaseline baseline)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        // Store under baseline.SessionKey (captured before the async render), not a key re-derived now:
        // a session switch during the render would otherwise file the snapshot under the wrong project.
        lock (_stateLock)
        {
            _qualityReviewBaselines[baseline.SessionKey] = baseline;
        }
    }

    public QualityReviewBaseline GetQualityReviewBaseline()
    {
        string currentKey = GetCompositionSessionKey();
        QualityReviewBaseline? baseline;
        lock (_stateLock)
        {
            _qualityReviewBaselines.TryGetValue(currentKey, out baseline);
        }

        if (baseline is null)
        {
            throw new ReconcileException(new ToolError(
                ErrorCode.StaleHandle,
                "No cached quality baseline exists for the current editing session.",
                "qualityBaseline",
                "Run evaluate_edit_quality or final_preflight with rendered sampling before compare_revisions."));
        }

        if (!StringComparer.Ordinal.Equals(baseline.SessionKey, currentKey))
        {
            throw new ReconcileException(new ToolError(
                ErrorCode.StaleHandle,
                "The cached quality baseline belongs to a different editing session.",
                "qualityBaseline",
                "Run evaluate_edit_quality or final_preflight again in the active session."));
        }

        return baseline;
    }

    private string GetCompositionSessionKey()
    {
        IEditingSession? session = CurrentSession;
        return session is null
            ? "host"
            : $"{session.Source}:{session.ReadOnSession(() => session.Root.Id)}";
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

public sealed record QualityReviewBaseline(
    string SessionKey,
    DateTimeOffset CreatedAt,
    IReadOnlyList<TimeSpan> SampleTimes,
    QualityAnalysisOptions Options,
    QualityReviewResponse Review,
    IReadOnlyList<string> StillPaths);

public sealed record QualityAnalysisOptions(
    string? VideoType,
    string? StyleProfile,
    float RenderScale,
    bool AllowAllCaps,
    bool AllowHardCuts,
    bool AllowRectDominance,
    bool RelaxAesthetics,
    bool AllowStillness,
    bool AllowDenseText,
    bool AllowMultiObjectElements,
    bool AllowMonochrome,
    bool AllowMinimalDensity,
    double PlannedForegroundElementsPerShot,
    IReadOnlyList<double>? BeatTimesSeconds,
    IReadOnlyList<PaletteRoleColor>? PaletteRoleColors);

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
