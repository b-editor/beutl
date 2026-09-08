namespace Beutl.Graphics.Rendering.Requests;

/// <summary>Reports that one node recorded two different graphs for the same request.</summary>
internal sealed class RenderRecordingCrossCheckException(Type nodeType, string difference)
    : InvalidOperationException(
        $"Render node '{nodeType.FullName ?? nodeType.Name}' recorded a different graph the second time it "
        + "was recorded for one request while reporting no changes. A recorded graph may be reused for a "
        + "node whose HasChanges is false, so this node would render stale: call MarkChanged() wherever "
        + $"it changes state its Process reads. Difference: {difference}")
{
    public Type NodeType { get; } = nodeType;
}
