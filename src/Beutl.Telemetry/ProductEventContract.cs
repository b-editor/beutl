using System.Collections.Frozen;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.RegularExpressions;

namespace Beutl.Services;

/// <summary>
/// Fixed v1 product event names. Arbitrary event names are intentionally rejected.
/// </summary>
internal static class ProductEventNames
{
    public const string AppSessionStart = "app.session.start";
    public const string AppSessionEnd = "app.session.end";
    public const string ProjectCreate = "project.create";
    public const string ProjectOpen = "project.open";
    public const string ProjectSave = "project.save";
    public const string AssetAdd = "asset.add";
    public const string EditorFirstEdit = "editor.first_edit";
    public const string EditorActionSummary = "editor.action_summary";
    public const string PreviewFirstFrame = "preview.first_frame";
    public const string PreviewPlaybackSummary = "preview.playback_summary";
    public const string MediaExport = "media.export";
    public const string ProjectPackageExport = "project.package_export";
    public const string ExtensionCatalog = "extension.catalog";
    public const string ExtensionManage = "extension.manage";
    public const string ExtensionLoad = "extension.load";
    public const string AgentInstall = "agent.install";
    public const string AgentHost = "agent.host";
    public const string AgentSessionAttach = "agent.session_attach";
    public const string AgentToolSummary = "agent.tool_summary";

    internal static readonly FrozenSet<string> All =
    [
        AppSessionStart, AppSessionEnd, ProjectCreate, ProjectOpen, ProjectSave, AssetAdd,
        EditorFirstEdit, EditorActionSummary, PreviewFirstFrame, PreviewPlaybackSummary, MediaExport,
        ProjectPackageExport, ExtensionCatalog, ExtensionManage, ExtensionLoad, AgentInstall,
        AgentHost, AgentSessionAttach, AgentToolSummary
    ];
}

internal static class ProductOutcomes
{
    public const string Success = "success";
    public const string Partial = "partial";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
    public const string Blocked = "blocked";
    public const string Queued = "queued";

    internal static readonly FrozenSet<string> All =
    [Success, Partial, Failed, Cancelled, Blocked, Queued];
}

internal static class ProductAttributeNames
{
    public const string Trigger = "beutl.trigger";
    public const string ErrorCode = "beutl.error_code";
    public const string DurationMilliseconds = "beutl.duration_ms";
    public const string FeatureId = "beutl.feature.id";
    public const string CountBucket = "beutl.count.bucket";
    public const string ResolutionBucket = "beutl.resolution.bucket";
    public const string ProjectSizeBucket = "beutl.project.size.bucket";

    internal static readonly FrozenSet<string> All =
    [
        Trigger, ErrorCode, DurationMilliseconds, FeatureId, CountBucket, ResolutionBucket,
        ProjectSizeBucket
    ];

    private static readonly FrozenSet<string> s_triggers =
    [
        "featured", "history-jump", "manual", "marketplace", "mcp", "menu", "more",
        "player", "preview", "reconcile", "settings", "startup", "undo", "redo", "unload"
    ];

    private static readonly FrozenSet<string> s_errorCodes =
    [
        "agent-install-failed", "agent-root-missing", "asset-open-failed", "cancelled",
        "catalog-load-failed", "cli-registration-partial", "create-failed", "editor-disposed",
        "encode-failed", "extension-install-failed", "extension-load-failed",
        "extension-unload-failed", "extension-unload-partial", "host-already-stopped",
        "host-start-failed", "host-stop-requested", "history-mutation-failed",
        "invalid-encoder-settings", "item-save-failed", "missing-controller", "missing-source",
        "no-history-change", "no-selected-item", "open-failed", "package-export-failed",
        "playback-failed", "playback-render-failed", "resource-relocation-partial",
        "save-all-failed", "session-attach-failed", "supersampling-limit", "tool-call-failed",
        "uncompleted-operation", "version-mismatch"
    ];

    private static readonly FrozenSet<string> s_countBuckets = ["1", "2-5", "6-10", "11-50", "51+"];
    private static readonly FrozenSet<string> s_resolutionBuckets = ["sd", "hd", "uhd", "larger"];
    private static readonly FrozenSet<string> s_projectSizeBuckets = ["1", "2-5", "6-10", "11-50", "51+"];
    private static readonly Regex s_featureIdPattern = new(
        "^(?:builtin/[a-z][a-z0-9-]{0,31}/[a-z][a-z0-9-]{0,63}|extension/[a-z0-9](?:[a-z0-9.-]{0,98}[a-z0-9])?/[a-z][a-z0-9-]{0,31}/[a-z][a-z0-9-]{0,63})$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    internal static bool IsAllowedValue(string key, string value)
    {
        return key switch
        {
            Trigger => s_triggers.Contains(value),
            ErrorCode => s_errorCodes.Contains(value),
            CountBucket => s_countBuckets.Contains(value),
            ResolutionBucket => s_resolutionBuckets.Contains(value),
            ProjectSizeBucket => s_projectSizeBuckets.Contains(value),
            FeatureId => value is "generic" or ProductSummaryBuffer.OverflowFeatureId
                || s_featureIdPattern.IsMatch(value),
            _ => false
        };
    }
}

/// <summary>
/// A disposable product operation. Calling <see cref="Complete"/> is optional;
/// uncompleted operations are reported as failed with a fixed contract error code.
/// </summary>
internal sealed class ProductOperation : IDisposable
{
    private readonly Activity? _activity;
    private readonly Activity? _previousActivity;
    private readonly Action<string, string?, double>? _complete;
    private readonly long _startedAt = Stopwatch.GetTimestamp();
    private int _completed;

    internal ProductOperation(
        Activity? activity,
        Action<string, string?, double>? complete,
        Activity? previousActivity = null)
    {
        _activity = activity;
        _complete = complete;
        _previousActivity = previousActivity;
    }

    public void Complete(
        string outcome = ProductOutcomes.Success,
        string? errorCode = null,
        double? durationMilliseconds = null)
    {
        if (Interlocked.Exchange(ref _completed, 1) == 0)
        {
            _complete?.Invoke(
                outcome,
                errorCode,
                durationMilliseconds ?? Stopwatch.GetElapsedTime(_startedAt).TotalMilliseconds);
            _activity?.Dispose();
            if (_activity is not null && Activity.Current is null)
            {
                Activity.Current = _previousActivity;
            }
        }
    }

    public void Dispose()
    {
        if (Volatile.Read(ref _completed) == 0)
        {
            Complete(ProductOutcomes.Failed, "uncompleted-operation");
        }
    }
}

/// <summary>
/// Represents one operation that contributes to an in-memory bounded summary.
/// It does not create an Activity per invocation.
/// </summary>
internal sealed class ProductSummaryOperation : IDisposable
{
    private readonly Action<string, string?, double>? _complete;
    private readonly long _startedAt = Stopwatch.GetTimestamp();
    private int _completed;

    internal ProductSummaryOperation(Action<string, string?, double>? complete)
    {
        _complete = complete;
    }

    public void Complete(string outcome = ProductOutcomes.Success, string? errorCode = null)
    {
        if (Interlocked.Exchange(ref _completed, 1) == 0)
        {
            _complete?.Invoke(outcome, errorCode, Stopwatch.GetElapsedTime(_startedAt).TotalMilliseconds);
        }
    }

    public void Dispose()
    {
        if (Volatile.Read(ref _completed) == 0)
        {
            Complete(ProductOutcomes.Failed, "uncompleted-operation");
        }
    }
}

internal static class ExtensionManageTelemetry
{
    internal static (string Outcome, string? ErrorCode) QueuedCompletion
        => (ProductOutcomes.Queued, null);

    internal static ExtensionManageProductOperation StartReconcile()
    {
        return new ExtensionManageProductOperation(Telemetry.StartProductOperation(
            ProductEventNames.ExtensionManage,
            [new(ProductAttributeNames.Trigger, "reconcile")]));
    }

    internal static void RecordQueued()
    {
        (string outcome, string? errorCode) = QueuedCompletion;
        Telemetry.RecordProductEvent(
            ProductEventNames.ExtensionManage,
            outcome,
            [new(ProductAttributeNames.Trigger, "reconcile")],
            errorCode);
    }
}

internal sealed class ExtensionManageProductOperation : IDisposable
{
    private readonly ProductOperation _operation;

    internal ExtensionManageProductOperation(ProductOperation operation)
    {
        _operation = operation;
    }

    internal ExtensionManageProductOperation(Action<string, string?> observeCompletion)
        : this(new ProductOperation(
            null,
            (outcome, errorCode, _) => observeCompletion(outcome, errorCode)))
    {
    }

    internal void CompleteSucceeded()
    {
        _operation.Complete(ProductOutcomes.Success);
    }

    internal void CompleteFailed()
    {
        _operation.Complete(ProductOutcomes.Failed, "extension-install-failed");
    }

    internal void CompleteCancelled()
    {
        _operation.Complete(ProductOutcomes.Cancelled, "cancelled");
    }

    internal void CompletePartial()
    {
        _operation.Complete(ProductOutcomes.Partial, "extension-unload-partial");
    }

    internal void CompleteUninstallFailed()
    {
        _operation.Complete(ProductOutcomes.Failed, "extension-unload-failed");
    }

    public void Dispose()
    {
        _operation.Dispose();
    }
}
