namespace Beutl.Graphics.Rendering;

internal sealed record LayerRenderFragmentPayload(Rect? Domain, bool DomainIsQueryFootprint = false);
