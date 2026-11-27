using System.ComponentModel;
using Beutl.AgentToolkit.Common;
using Beutl.Editor.Services;
using Beutl.Language;
using Beutl.Media;
using Beutl.ProjectSystem;
using ModelContextProtocol.Server;

namespace Beutl.AgentToolkit.Tools;

public sealed partial class TimelineEditTools
{
    [McpServerTool(Name = "duplicate_elements")]
    [Description("Duplicates timeline elements and their group members using the editor's nearby free placement search. Preserves relative positions, layers and groups among the copies. Returns the new element IDs and reports any locked sources skipped.")]
    public ValueTask<ToolResult<TimelineEditResponse>> DuplicateElements(
        [Description("Element IDs from read_document_summary. Group members are included automatically.")]
        string[] elementIds,
        [Description("Destination start in seconds from timeline zero, rounded to the nearest frame.")]
        double timeSeconds,
        [Description("Destination layer index (ZIndex) for the lowest-indexed copy.")]
        int layer,
        CancellationToken cancellationToken = default)
        => EditElements(elementIds, CommandNames.DuplicateElement, (scene, history, elements) =>
        {
            if (scene.Uri is null) throw InvalidEdit(Strings.Duplicate_ProjectNotSaved, null);
            long lastLayer = layer + (long)elements.Max(element => element.ZIndex.Value) - elements.Min(element => element.ZIndex.Value);
            if (layer < 0 || lastLayer >= int.MaxValue)
                throw InvalidEdit(Strings.Duplicate_InvalidLayer, nameof(layer));

            MediaTime at = ReadFrameTime(scene, timeSeconds, nameof(timeSeconds));
            HashSet<Element> originals = [.. scene.Children];
            DuplicateOutcome outcome = new ElementDuplicateService(history, configuration.EditorConfig)
                .DuplicateAtClickedPosition(scene, elements, at, layer);
            // The editor service reports insertion failures as an unsuccessful outcome. Throw here so any
            // work it did before the failure is rolled back by the session transaction.
            if (!outcome.Success) throw InvalidEdit(Strings.Duplicate_Failed, nameof(elementIds));

            return new TimelineEditResponse(true, CreatedElementIds: ElementIds(scene.Children.Where(element => !originals.Contains(element))));
        }, cancellationToken, includeLinked: true);

    [McpServerTool(Name = "ripple_trim")]
    [Description("Moves an element edge and ripples neighboring clips on affected layers. Set includeLinked to also trim group members with the same moving edge. The editor may clamp the requested position. Returns IDs of trimmed and shifted elements.")]
    public ValueTask<ToolResult<TimelineEditResponse>> RippleTrim(
        [Description("Element ID from read_document_summary.")]
        string elementId,
        [Description("Edge to move: start or end.")]
        string edge,
        [Description("New edge position in seconds from timeline zero, rounded to the nearest frame.")]
        double timeSeconds,
        [Description("Also trim unlocked group members with the same start or end as the requested edge. Defaults to false.")]
        bool includeLinked = false,
        CancellationToken cancellationToken = default)
        => EditTimeline(CommandNames.MoveElement, (scene, history) =>
        {
            Element anchor = ReadElement(scene, elementId);
            if (scene.IsElementLocked(anchor)) throw LockedEdit(scene, anchor);

            bool leftEdge = ReadTrimStart(edge);
            MediaTime at = ReadFrameTime(scene, timeSeconds, nameof(timeSeconds));
            IEnumerable<Element> members = includeLinked
                ? TrimGroupCollector.CollectResizeMembers(FindTrimMembers(scene, anchor), anchor, leftEdge)
                : [anchor];

            return EditUnlockedElements(scene, members, elements =>
            {
                if (at == TrimGroupCollector.GetEdge(anchor, leftEdge)) return new TimelineEditResponse(false);

                ElementResizeRequest[] requests = elements.Select(element =>
                {
                    TimeRange range = TrimGroupCollector.GetResizeRange(element.Start.Value, element.Range.End, at, leftEdge);
                    return new ElementResizeRequest(element, range.Start, range.Duration, element.ZIndex.Value);
                }).ToArray();
                var before = scene.Children.Select(element => (Element: element, Range: element.Range)).ToArray();
                MediaTime duration = scene.Duration.Value;
                new ElementResizeService(history, configuration.EditorConfig, decoders).Resize(scene, requests, ripple: true);
                string[] changed = ElementIds(before.Where(item => item.Element.Range != item.Range).Select(item => item.Element));
                return new TimelineEditResponse(changed.Length > 0 || scene.Duration.Value != duration) { ChangedElementIds = changed };
            });
        }, cancellationToken);

    [McpServerTool(Name = "roll_elements")]
    [Description("Moves the cut at an element edge and the aligned cuts of its group members by a shared amount. Adjacent clips compensate to preserve total length and media continuity. A locked participant blocks the whole edit. Returns the changed element IDs.")]
    public ValueTask<ToolResult<TimelineEditResponse>> RollElements(
        [Description("Element ID from read_document_summary on one side of the cut.")]
        string elementId,
        [Description("Edge to roll: start or end. A neighboring clip must share this edge.")]
        string edge,
        [Description("Signed cut shift in timeline seconds, rounded to the nearest frame. Positive moves the cut later.")]
        double deltaSeconds,
        CancellationToken cancellationToken = default)
        => EditTimeline(CommandNames.RollElements, (scene, history) =>
        {
            Element anchor = ReadElement(scene, elementId);
            if (scene.IsElementLocked(anchor)) throw LockedEdit(scene, anchor);

            bool leftEdge = ReadTrimStart(edge);
            MediaTime delta = ReadFrameTime(scene, deltaSeconds, nameof(deltaSeconds), signed: true);
            var pairs = TrimGroupCollector.CollectRollPairs(scene, FindTrimMembers(scene, anchor), anchor, leftEdge, out Element? locked);
            if (locked is not null) throw LockedEdit(scene, locked);
            if (pairs is null || !new ElementResizeService(history, configuration.EditorConfig, decoders).Roll(scene, pairs, delta))
                return new TimelineEditResponse(false);

            return new TimelineEditResponse(true)
            {
                ChangedElementIds = ElementIds(pairs.SelectMany(pair => new[] { pair.Front, pair.Back }))
            };
        }, cancellationToken);

    [McpServerTool(Name = "slide_elements")]
    [Description("Slides elements and their group members while trimming adjacent neighbors to preserve total length and media continuity. Each participating layer needs one contiguous block with an editable clip on each side. A locked participant blocks the whole edit. Returns the changed element IDs.")]
    public ValueTask<ToolResult<TimelineEditResponse>> SlideElements(
        [Description("Element IDs from read_document_summary for the clips to slide. Group members are included automatically.")]
        string[] elementIds,
        [Description("Signed shift in timeline seconds, rounded to the nearest frame. Positive moves the block later.")]
        double deltaSeconds,
        CancellationToken cancellationToken = default)
        => EditTimeline(CommandNames.SlideElements, (scene, history) =>
        {
            Element[] members = ReadElements(scene, elementIds, includeLinked: true);
            MediaTime delta = ReadFrameTime(scene, deltaSeconds, nameof(deltaSeconds), signed: true);
            var lanes = TrimGroupCollector.CollectSlideLanes(scene, members, out Element? locked);
            if (locked is not null) throw LockedEdit(scene, locked);
            if (lanes is null || !new ElementResizeService(history, configuration.EditorConfig, decoders).Slide(scene, lanes, delta))
                return new TimelineEditResponse(false);

            return new TimelineEditResponse(true)
            {
                ChangedElementIds = ElementIds(lanes.SelectMany(lane => lane.Middles.Prepend(lane.Front).Append(lane.Back)))
            };
        }, cancellationToken);

    private static Element[] FindTrimMembers(Scene scene, Element anchor)
    {
        Element[] group = DuplicateHelper.FindWithGroupSiblings(scene, [], anchor.Id.Value);
        return group.Length == 0 ? [anchor] : group;
    }

    private static bool ReadTrimStart(string edge) => edge switch
    {
        "start" => true,
        "end" => false,
        _ => throw InvalidEdit("Choose 'start' or 'end'.", nameof(edge))
    };
}
