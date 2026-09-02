namespace Beutl.AgentToolkit.Tools;

public sealed record ObjectBoundsMeasurementResponse(
    string SchemaVersion,
    string Session,
    string Source,
    string SceneId,
    int FrameWidth,
    int FrameHeight,
    ObjectBoundsPoint FrameCenter,
    string Time,
    bool TimeFiltered,
    string CoordinateSpace,
    string MeasurementNote,
    IReadOnlyList<ObjectBoundsMeasurement> Objects);
