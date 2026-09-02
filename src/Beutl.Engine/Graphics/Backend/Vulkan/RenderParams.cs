using Beutl.Configuration;
using Beutl.Media;

namespace Beutl.Graphics.Backend.Vulkan;

/// <summary>
/// Parameters for bitmap rendering.
/// </summary>
internal readonly record struct RenderParams(
    float SourceWidth,
    float SourceHeight,
    float DestWidth,
    float DestHeight,
    Stretch Stretch,
    UIToneMappingOperator ToneMapping,
    float Exposure,
    bool IsSourceLinear);
