using Beutl.Configuration;
using Beutl.Media;

namespace Beutl.Graphics.Backend.Vulkan;

/// <summary>
/// Parameters for bitmap rendering.
/// </summary>
/// <param name="DisplayGamut">What FitToDisplay maps colors into on an HDR swapchain, which can carry colors outside
/// sRGB; <see cref="PreviewGamutMapping.None"/> leaves them for the display to clip.</param>
internal readonly record struct RenderParams(
    float SourceWidth,
    float SourceHeight,
    Stretch Stretch,
    UIToneMappingOperator ToneMapping,
    float Exposure,
    bool IsSourceLinear,
    PreviewGamutMapping DisplayGamut = default);
