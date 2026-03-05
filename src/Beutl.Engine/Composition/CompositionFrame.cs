using System.Collections.Immutable;
using Beutl.Engine;
using Beutl.Media;

namespace Beutl.Composition;

public readonly record struct CompositionFrame(
    ImmutableArray<EngineObject.Resource> Objects,
    TimeRange Time,
    PixelSize Size);
