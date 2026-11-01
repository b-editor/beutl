namespace Beutl.Graphics.Backend.Vulkan;

/// <summary>What a Windows display in HDR mode reports, in nit, with 0 for a value it does not.</summary>
/// <param name="SdrWhiteNits">The SDR white level, which the UI's white is shown at.</param>
/// <param name="PeakNits">The peak luminance, which DXGI mostly reads from the EDID.</param>
internal readonly record struct HdrDisplayReport(float SdrWhiteNits, float PeakNits);
