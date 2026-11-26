using System.ComponentModel;
using System.Text.Json.Serialization;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Sessions;
using Beutl.Configuration;
using Beutl.Editor;
using Beutl.Editor.Services;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.ProjectSystem;
using ModelContextProtocol.Server;

namespace Beutl.AgentToolkit.Tools;

public sealed record TimelineEditResponse(
    bool Changed,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? CreatedElementIds = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? SkippedLockedElementIds = null);

[McpServerToolType]
public sealed class TimelineEditTools(
    AgentSessionManager sessions, GlobalConfiguration configuration, DecoderRegistry decoders) : ToolBase
{
    [McpServerTool(Name = "split_elements")]
    [Description("Splits the specified elements at one timeline time and returns the IDs of the new right halves. Each half must span at least one frame.")]
    public ToolResult<TimelineEditResponse> SplitElements(
        [Description("Element IDs from read_document_summary. Include every clip that should be split together.")]
        string[] elementIds,
        [Description("Cut position in seconds from timeline zero, rounded to the nearest frame.")]
        double timeSeconds,
        CancellationToken cancellationToken = default)
        => EditElements(elementIds, (scene, history, elements) =>
        {
            MediaTime at = ReadFrameTime(scene, timeSeconds, nameof(timeSeconds));
            HashSet<Element> originalChildren = [.. scene.Children];
            new ElementStructureService(history, configuration.EditorConfig, decoders).Split(scene, elements, at);
            string[] created = scene.Children.Where(element => !originalChildren.Contains(element))
                .Select(element => element.Id.Value.ToString()).ToArray();
            return new TimelineEditResponse(created.Length > 0, CreatedElementIds: created);
        }, cancellationToken);

    [McpServerTool(Name = "slip_elements")]
    [Description("Shifts the source media windows of the specified elements by a shared amount while keeping their timeline positions and lengths. The editor clamps the shift to available media.")]
    public ToolResult<TimelineEditResponse> SlipElements(
        [Description("Element IDs from read_document_summary. Include linked clips to keep their source windows in sync.")]
        string[] elementIds,
        [Description("Signed shift in timeline seconds, rounded to the nearest frame. Positive values use later source media.")]
        double deltaSeconds,
        CancellationToken cancellationToken = default)
        => EditElements(elementIds, (scene, history, elements) =>
        {
            MediaTime delta = ReadFrameTime(scene, deltaSeconds, nameof(deltaSeconds), signed: true);
            bool changed = new ElementSlipService(history, decoders).Slip(scene, elements, delta);
            return new TimelineEditResponse(changed);
        }, cancellationToken);

    [McpServerTool(Name = "ripple_delete")]
    [Description("Deletes the specified elements and shifts following clips left on their respective layers by the removed durations, subject to timeline locks.")]
    public ToolResult<TimelineEditResponse> RippleDelete(
        [Description("Element IDs from read_document_summary to delete together.")]
        string[] elementIds,
        CancellationToken cancellationToken = default)
        => EditElements(elementIds, (scene, history, elements) =>
        {
            new ElementStructureService(history, configuration.EditorConfig, decoders).Delete(scene, elements, ripple: true);
            return new TimelineEditResponse(true);
        }, cancellationToken, removingElements: true);

    [McpServerTool(Name = "ripple_trim")]
    [Description("Moves one element edge and ripples neighboring clips on the same layer. The editor may clamp the requested edge position.")]
    public ToolResult<TimelineEditResponse> RippleTrim(
        [Description("Element ID from read_document_summary.")]
        string elementId,
        [Description("Edge to move: start or end.")]
        string edge,
        [Description("New edge position in seconds from timeline zero, rounded to the nearest frame.")]
        double timeSeconds,
        CancellationToken cancellationToken = default)
        => EditElements([elementId], (scene, history, elements) =>
        {
            Element element = elements[0];
            bool trimStart = edge switch
            {
                "start" => true,
                "end" => false,
                _ => throw InvalidEdit("Choose 'start' or 'end'.", nameof(edge))
            };
            MediaTime at = ReadFrameTime(scene, timeSeconds, nameof(timeSeconds));
            TimeRange original = element.Range;
            MediaTime start = trimStart ? at : original.Start;
            MediaTime end = trimStart ? original.End : at;
            if (start == original.Start && end == original.End) return new TimelineEditResponse(false);

            var request = new ElementResizeRequest(element, start, end - start, element.ZIndex.Value);
            HistoryTransaction? before = history.PeekUndo();
            new ElementResizeService(history, configuration.EditorConfig, decoders).Resize(scene, [request], ripple: true);
            return new TimelineEditResponse(history.PeekUndo() != before);
        }, cancellationToken);

    [McpServerTool(Name = "close_gap")]
    [Description("Closes the first gap after an element's contiguous run on its layer by shifting unlocked followers left up to the first locked clip.")]
    public ToolResult<TimelineEditResponse> CloseGap(
        [Description("Element ID before the gap, from read_document_summary. The anchor itself stays in place.")]
        string elementId,
        CancellationToken cancellationToken = default)
        => EditTimeline((scene, history) =>
        {
            Guid id = ReadElementId(elementId);
            Element anchor = RequireElement(scene.Children.Where(element => element.Id.Value == id), id);
            bool changed = new ElementGapService(history, configuration.EditorConfig).CloseGapAfter(scene, anchor);
            return new TimelineEditResponse(changed);
        }, cancellationToken);

    private ToolResult<TimelineEditResponse> EditElements(
        string[] elementIds,
        Func<Scene, HistoryManager, Element[], TimelineEditResponse> edit,
        CancellationToken cancellationToken,
        bool removingElements = false)
        => EditTimeline((scene, history) =>
        {
            if (elementIds is not { Length: > 0 })
                throw InvalidEdit("Supply at least one element ID.", nameof(elementIds));

            var byId = scene.Children.ToLookup(element => element.Id.Value);
            var editable = new List<Element>();
            var locked = new List<Element>();
            foreach (Guid id in elementIds.Select(ReadElementId).Distinct())
            {
                Element element = RequireElement(byId[id], id);
                // A removal leaves the retained file intact. Like the UI, it checks only user locks.
                bool isLocked = removingElements ? scene.IsElementLockedByUser(element) : scene.IsElementLocked(element);
                (isLocked ? locked : editable).Add(element);
            }

            if (editable.Count == 0)
            {
                Element firstLocked = locked[0];
                throw new ReconcileException(Reconciler.CreateLockedElementError(scene, firstLocked, firstLocked.Id.Value.ToString()));
            }

            TimelineEditResponse result = edit(scene, history, [.. editable]);
            return locked.Count == 0 ? result : result with
            {
                SkippedLockedElementIds = locked.Select(element => element.Id.Value.ToString()).ToArray()
            };
        }, cancellationToken);

    private ToolResult<TimelineEditResponse> EditTimeline(
        Func<Scene, HistoryManager, TimelineEditResponse> edit, CancellationToken cancellationToken)
        => Execute(() =>
        {
            IEditingSession session = sessions.RequireSession();
            return session.EditOnSession(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                Scene scene = RequireSceneRoot(session);
                HistoryManager history = session.History;
                // The services commit their own entries. Isolate them from pending editor edits.
                history.FlushPendingMutations();
                if (history.HasPendingOperations) history.Commit();

                TimelineEditResponse result;
                try
                {
                    result = edit(scene, history);
                }
                catch
                {
                    history.Rollback();
                    throw;
                }

                if (result.Changed) session.MarkDirtyIfFileSession();
                return result;
            }, cancellationToken);
        });

    private static Guid ReadElementId(string value)
        => Guid.TryParse(value, out Guid id) && id != Guid.Empty
            ? id
            : throw InvalidEdit("Supply a valid element ID from read_document_summary.", value);

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
}
