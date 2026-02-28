using System.Collections.Immutable;
using Beutl.Engine;
using Beutl.Media;

namespace Beutl.Graphics.Rendering;

public readonly record struct CompositionFrame(
    ImmutableArray<EngineObject.Resource> Objects,
    TimeSpan Time,
    PixelSize Size);
