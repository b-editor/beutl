namespace Beutl.AgentToolkit.Tools;

public sealed record ObjectBoundsMeasurement(
    string ElementId,
    string ElementName,
    string ElementStart,
    string ElementLength,
    int ElementZIndex,
    string ObjectId,
    string ObjectName,
    string Type,
    bool IsEnabled,
    string AlignmentX,
    string AlignmentY,
    string MeasurementKind,
    ObjectBoundsRect LocalBounds,
    ObjectBoundsRect TransformedBounds,
    ObjectBoundsPoint Center,
    ObjectBoundsPoint? UserTranslate,
    ObjectTransformMatrix UserTransformMatrix,
    string? Note = null,
    ObjectBoundsPoint? GeometryBoundsOrigin = null);
