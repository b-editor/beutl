using System.ComponentModel;
using System.Text.Json.Serialization;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Sessions;
using Beutl.Configuration;
using Beutl.Editor;
using Beutl.Editor.Services;
using Beutl.Language;
using Beutl.Media.Decoding;
using Beutl.ProjectSystem;
using ModelContextProtocol.Server;

namespace Beutl.AgentToolkit.Tools;

public sealed record TimelineEditResponse(
    bool Changed,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? CreatedElementIds = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? SkippedLockedElementIds = null)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? ChangedElementIds { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<TimelineSilenceRange>? RemovedRanges { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ClosedGapCount { get; init; }
}

public sealed record TimelineGap(int Layer, double StartSeconds, double EndSeconds);

public sealed record FindGapsResponse(IReadOnlyList<TimelineGap> Gaps);

[McpServerToolType]
public sealed partial class TimelineEditTools(
    AgentSessionManager sessions, GlobalConfiguration configuration, DecoderRegistry decoders) : ToolBase
{
    [McpServerTool(Name = "split_elements")]
    [Description("Splits the specified elements at one timeline time and returns the IDs of the new right halves. Each half must span at least one frame.")]
    public ValueTask<ToolResult<TimelineEditResponse>> SplitElements(
        [Description("Element IDs from read_document_summary. Include every clip that should be split together.")]
        string[] elementIds,
        [Description("Cut position in seconds from timeline zero, rounded to the nearest frame.")]
        double timeSeconds,
        CancellationToken cancellationToken = default)
        => EditElements(elementIds, CommandNames.SplitElement, (scene, history, elements) =>
        {
            MediaTime at = ReadFrameTime(scene, timeSeconds, nameof(timeSeconds));
            HashSet<Element> originalChildren = [.. scene.Children];
            new ElementStructureService(history, configuration.EditorConfig, decoders).Split(scene, elements, at);
            string[] created = ElementIds(scene.Children.Where(element => !originalChildren.Contains(element)));
            return new TimelineEditResponse(created.Length > 0, CreatedElementIds: created);
        }, cancellationToken);

    [McpServerTool(Name = "slip_elements")]
    [Description("Shifts the source media windows of the specified elements by a shared amount while keeping their timeline positions and lengths. The editor clamps the shift to available media.")]
    public ValueTask<ToolResult<TimelineEditResponse>> SlipElements(
        [Description("Element IDs from read_document_summary. Include linked clips to keep their source windows in sync.")]
        string[] elementIds,
        [Description("Signed shift in timeline seconds, rounded to the nearest frame. Positive values use later source media.")]
        double deltaSeconds,
        CancellationToken cancellationToken = default)
        => EditElements(elementIds, CommandNames.SlipElement, (scene, history, elements) =>
        {
            MediaTime delta = ReadFrameTime(scene, deltaSeconds, nameof(deltaSeconds), signed: true);
            bool changed = new ElementSlipService(history, decoders).Slip(scene, elements, delta);
            return new TimelineEditResponse(changed);
        }, cancellationToken);

    [McpServerTool(Name = "ripple_delete")]
    [Description("Deletes the specified elements and shifts following clips left on their respective layers by the removed durations, subject to timeline locks.")]
    public ValueTask<ToolResult<TimelineEditResponse>> RippleDelete(
        [Description("Element IDs from read_document_summary to delete together.")]
        string[] elementIds,
        CancellationToken cancellationToken = default)
        => EditElements(elementIds, CommandNames.DeleteElement, (scene, history, elements) =>
        {
            new ElementStructureService(history, configuration.EditorConfig, decoders).Delete(scene, elements, ripple: true);
            return new TimelineEditResponse(true);
        }, cancellationToken, removingElements: true);

    [McpServerTool(Name = "close_gap")]
    [Description("Closes the first gap after an element's contiguous run on its layer by shifting unlocked followers left up to the first locked clip.")]
    public ValueTask<ToolResult<TimelineEditResponse>> CloseGap(
        [Description("Element ID before the gap, from read_document_summary. The anchor itself stays in place.")]
        string elementId,
        CancellationToken cancellationToken = default)
        => EditTimeline(CommandNames.CloseGap, (scene, history) =>
        {
            Element anchor = ReadElement(scene, elementId);
            bool changed = new ElementGapService(history, configuration.EditorConfig).CloseGapAfter(scene, anchor);
            return new TimelineEditResponse(changed);
        }, cancellationToken);

    [McpServerTool(Name = "find_gaps", ReadOnly = true)]
    [Description("Lists gaps between clips per layer, in timeline seconds. Includes gaps blocked by locks. Leading and trailing space is excluded.")]
    public ToolResult<FindGapsResponse> FindGaps(
        [Description("Optional timeline layer indices (ZIndex). Omit to inspect all layers.")]
        int[]? layers = null)
        => Execute(() =>
        {
            IEditingSession session = sessions.RequireSession();
            HashSet<int>? selectedLayers = layers?.ToHashSet();
            return session.ReadOnSession(() => new FindGapsResponse(
                RequireSceneRoot(session).EnumerateGaps()
                    .Where(gap => selectedLayers == null || selectedLayers.Contains(gap.ZIndex))
                    .Select(gap => new TimelineGap(gap.ZIndex, gap.Range.Start.ToSeconds(), gap.Range.End.ToSeconds()))
                    .ToArray()));
        });

    [McpServerTool(Name = "close_all_gaps")]
    [Description("Closes gaps between clips on the selected layers, shifting unlocked followers left. Locked layers are skipped and locked clips act as barriers. Returns the number of gaps closed.")]
    public ValueTask<ToolResult<TimelineEditResponse>> CloseAllGaps(
        [Description("Optional timeline layer indices (ZIndex). Omit to edit all layers.")]
        int[]? layers = null,
        CancellationToken cancellationToken = default)
        => EditTimeline(CommandNames.CloseAllGaps, (scene, history) =>
        {
            int closed = new ElementGapService(history, configuration.EditorConfig).CloseAllGaps(scene, layers?.ToHashSet());
            return new TimelineEditResponse(closed > 0) { ClosedGapCount = closed };
        }, cancellationToken);

    private ValueTask<ToolResult<TimelineEditResponse>> EditElements(
        string[] elementIds,
        string commandName,
        Func<Scene, HistoryManager, Element[], TimelineEditResponse> edit,
        CancellationToken cancellationToken,
        bool removingElements = false,
        bool includeLinked = false)
        => EditTimeline(commandName, (scene, history) => EditUnlockedElements(scene,
            ReadElements(scene, elementIds, includeLinked), elements => edit(scene, history, elements), removingElements),
            cancellationToken);

    private static TimelineEditResponse EditUnlockedElements(Scene scene, IEnumerable<Element> elements,
        Func<Element[], TimelineEditResponse> edit, bool removingElements = false)
    {
        var editable = new List<Element>();
        var locked = new List<Element>();
        foreach (Element element in elements)
        {
            // A removal leaves the retained file intact. Like the UI, it checks only user locks.
            bool isLocked = removingElements ? scene.IsElementLockedByUser(element) : scene.IsElementLocked(element);
            (isLocked ? locked : editable).Add(element);
        }

        if (editable.Count == 0) throw LockedEdit(scene, locked[0]);

        TimelineEditResponse result = edit([.. editable]);
        return locked.Count == 0 ? result : result with { SkippedLockedElementIds = ElementIds(locked) };
    }

    private ValueTask<ToolResult<TimelineEditResponse>> EditTimeline(
        string commandName, Func<Scene, HistoryManager, TimelineEditResponse> edit, CancellationToken cancellationToken)
        => ExecuteAsync(() => ApplyTimelineAsync(sessions.RequireSession(), commandName, edit, cancellationToken));

    private static async ValueTask<TimelineEditResponse> ApplyTimelineAsync(
        IEditingSession session, string commandName, Func<Scene, HistoryManager, TimelineEditResponse> edit,
        CancellationToken cancellationToken)
    {
        if (session is LiveEditingSession live)
        {
            return await live.ExecuteHistoryMutationAsync(
                static _ => true,
                history =>
                {
                    // The user can start another gesture while playback drains.
                    if (history.IsEditing) throw new UserEditingException();
                    return ApplyEdit(history);
                },
                cancellationToken, waitForEditSession: true).ConfigureAwait(false);
        }

        return session.EditOnSession(() => ApplyEdit(session.History), cancellationToken);

        TimelineEditResponse ApplyEdit(HistoryManager history)
        {
            Scene scene = RequireSceneRoot(session);
            TimelineEditResponse result = new(false);
            history.ExecuteInTransaction(() =>
            {
                var checkTransitions = scene.HasTransitions
                    ? Reconciler.CaptureLockedTransitionCheck(scene, [.. scene.Children.Where(scene.IsElementLockedByUser)])
                    : null;
                // The session records the mutation. An unsubscribed history lets services issue their
                // own commits while the session can still roll back a rejected edit.
                using var serviceHistory = new HistoryManager(new OperationSequenceGenerator());
                result = edit(scene, serviceHistory);
                if (result.Changed && checkTransitions?.Invoke(scene) is { } error)
                {
                    throw new ReconcileException(error);
                }
            }, commandName, cancellationToken: cancellationToken);
            if (result.Changed) session.MarkDirtyIfFileSession();
            return result;
        }
    }

    private static Guid ReadElementId(string value)
        => Guid.TryParse(value, out Guid id) && id != Guid.Empty
            ? id
            : throw InvalidEdit("Supply a valid element ID from read_document_summary.", value);

    private static Element ReadElement(Scene scene, string elementId)
    {
        Guid id = ReadElementId(elementId);
        return RequireElement(scene.Children.Where(element => element.Id.Value == id), id);
    }

    internal static Element[] ReadElements(Scene scene, string[] elementIds, bool includeLinked)
    {
        if (elementIds is not { Length: > 0 })
            throw InvalidEdit("Supply at least one element ID.", nameof(elementIds));

        var byId = scene.Children.ToLookup(element => element.Id.Value);
        Guid[] ids = elementIds.Select(ReadElementId).Distinct().ToArray();
        Element[] selected = ids.Select(id => RequireElement(byId[id], id)).ToArray();
        if (!includeLinked) return selected;

        Element[] members = DuplicateHelper.FindWithGroupSiblings(scene, ids);
        foreach (Element member in members)
        {
            Guid id = member.Id.Value;
            RequireElement(byId[id], id);
        }

        return members;
    }

    private static Element RequireElement(IEnumerable<Element> matches, Guid id)
    {
        using IEnumerator<Element> candidates = matches.GetEnumerator();
        if (!candidates.MoveNext())
            throw new ReconcileException(new ToolError(ErrorCode.StaleHandle,
                $"Element '{id}' was not found in the target scene.", id.ToString()));

        Element element = candidates.Current;
        if (candidates.MoveNext())
            throw InvalidEdit($"More than one element has ID '{id}' in the target scene.", id.ToString());

        return element;
    }

    private static MediaTime ReadFrameTime(Scene scene, double seconds, string target, bool signed = false)
    {
        if (!double.IsFinite(seconds) || (!signed && seconds < 0))
            throw InvalidEdit(signed ? "Supply a finite number of seconds." : "Supply finite, non-negative seconds.", target);

        try
        {
            return MediaTime.FromSeconds(seconds).RoundToFrame(
                SceneTimeRangeService.GetFrameRate(scene), MidpointRounding.AwayFromZero);
        }
        catch (OverflowException)
        {
            throw InvalidEdit("The time in seconds is outside the supported timeline range.", target);
        }
    }

    private static ReconcileException InvalidEdit(string message, string? target)
        => new(new ToolError(ErrorCode.ValidationRejected, message, target));

    private static ReconcileException LockedEdit(Scene scene, Element element)
        => new(Reconciler.CreateLockedElementError(scene, element, element.Id.Value.ToString()));

    private static string[] ElementIds(IEnumerable<Element> elements)
        => elements.Select(element => element.Id.Value.ToString()).ToArray();
}
