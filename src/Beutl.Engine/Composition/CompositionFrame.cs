using System.Collections.Immutable;
using Beutl.Engine;
using Beutl.Media;

namespace Beutl.Composition;

/// <summary>Captures resources and target eligibility for one evaluated composition range.</summary>
/// <param name="Objects">Resources whose time ranges intersect the evaluated range.</param>
/// <param name="Time">The evaluated composition range.</param>
/// <param name="Size">The output pixel size.</param>
/// <param name="Eligibility">
/// Original objects currently eligible for the composition target, including objects outside
/// <paramref name="Time"/>.
/// </param>
public readonly record struct CompositionFrame(
    ImmutableArray<EngineObject.Resource> Objects,
    TimeRange Time,
    PixelSize Size,
    CompositionEligibility Eligibility);
