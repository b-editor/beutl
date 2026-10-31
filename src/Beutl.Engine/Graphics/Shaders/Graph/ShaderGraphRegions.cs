using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.Graphics.Shaders;

/// <summary>One request's complete coordinate frames and conservative per-pass evaluation rectangles.</summary>
internal sealed class ShaderGraphRegions
{
    public Dictionary<int, ShaderImageFrame> Frames { get; } = [];
    public Rect?[] PassRegions { get; }
    public Rect SourceRegion { get; }

    public ShaderGraphRegions(ShaderGraphDescription graph, ShaderImageFrame source, float scale,
        Vector gridOffset, Rect requiredOutput)
    {
        Frames.Add(0, source);
        foreach (ShaderImage image in graph.Resources.OfType<ShaderImage>())
        {
            if (image.Id == 0 || graph.LastUses[image.Id] < 0) continue;
            if (image.RasterSource is { } rasterSource)
            {
                Frames.Add(image.Id, Frames[rasterSource.Id]);
                continue;
            }
            Rect bounds = image.ResolveBounds(source.Bounds, source.ReferenceBounds);
            RenderRectValidation.ThrowIfInvalidInput(bounds, nameof(graph));
            float density = image.Description.HasValue ? 1 : scale * image.ResolutionScale;
            PixelRect pixels = image.Description is { } fixedSize
                ? new(0, 0, fixedSize.Width, fixedSize.Height)
                : PixelRect.FromRect(bounds.Translate(gridOffset), density);
            if (pixels.Width <= 0 || pixels.Height <= 0)
                throw new InvalidOperationException($"Image '{image.Name}' has empty dimensions.");
            Frames.Add(image.Id, image.Description.HasValue ? new(bounds, bounds, pixels.Size, density)
                : ShaderImageFrame.FromPixels(bounds, pixels, density, gridOffset));
        }
        PassRegions = new Rect?[graph.Passes.Length];
        var required = new Dictionary<int, Rect>();
        ShaderImage selected = graph.Output.RenderingImage;
        Require(selected, requiredOutput.Inflate(2 / Frames[selected.Id].Scale));
        for (int index = graph.Passes.Length - 1; index >= 0; index--)
        {
            ShaderPassDescription pass = graph.Passes[index];
            bool local = pass.PartialRendering && pass.Program is { IsCompute: false }
                && pass.DepthAttachment is null
                && pass.Attachments.All(a => IsPlanar(a.Image))
                && pass.Bindings.All(b => b.Metadata || (b.Access & ResourceAccess.Write) == 0
                    && (b.Resource is not ShaderImage image || IsPlanar(image) && b.Type == DescriptorType.CombinedImageSampler));
            Rect output = Rect.Empty;
            if (local)
            {
                foreach (ShaderColorAttachment attachment in pass.Attachments)
                    if (required.TryGetValue(attachment.Image.Root.Id, out Rect region)) output = Union(output, region);
                if (!output.IsEmpty)
                {
                    ShaderImageFrame frame = Frames[pass.Attachments[0].Image.Id];
                    PixelRect pixels = Scissor(frame, output);
                    output = new Rect(frame.RasterBounds.X + pixels.X / frame.Scale,
                        frame.RasterBounds.Y + pixels.Y / frame.Scale, pixels.Width / frame.Scale, pixels.Height / frame.Scale);
                }
                PassRegions[index] = output;
            }
            foreach (ShaderPassBinding binding in pass.Bindings)
            {
                if (binding.Metadata || binding.Resource is not ShaderImage image || (binding.Access & ResourceAccess.Read) == 0) continue;
                Rect input = Frames[image.Id].Bounds;
                if (local && pass.InputRegions.TryGetValue(binding.Binding, out ShaderInputRegion contract)
                    && binding.Sampler.AddressU is SamplerAddressMode.ClampToEdge or SamplerAddressMode.ClampToBorder
                    && binding.Sampler.AddressV is SamplerAddressMode.ClampToEdge or SamplerAddressMode.ClampToBorder)
                {
                    if (output.IsEmpty) continue;
                    input = contract.Map(output);
                    Rect bounds = Frames[image.Id].RasterBounds;
                    float left = input.X, right = input.Right, top = input.Y, bottom = input.Bottom;
                    if (binding.Sampler.AddressU == SamplerAddressMode.ClampToEdge)
                    {
                        left = Math.Clamp(left, bounds.X, bounds.Right); right = Math.Clamp(right, bounds.X, bounds.Right);
                    }
                    if (binding.Sampler.AddressV == SamplerAddressMode.ClampToEdge)
                    {
                        top = Math.Clamp(top, bounds.Y, bounds.Bottom); bottom = Math.Clamp(bottom, bounds.Y, bounds.Bottom);
                    }
                    // Include filtering neighbors and a raster-grid phase on each side.
                    input = new Rect(left, top, right - left, bottom - top).Inflate(2 / Frames[image.Id].Scale);
                }
                Require(image, input);
            }
            if (pass.Transfer?.Source is ShaderImage transferSource)
                Require(transferSource, Frames[transferSource.Id].Bounds);
        }
        SourceRegion = required.GetValueOrDefault(0);

        void Require(ShaderImage image, Rect region)
        {
            region = region.Intersect(Frames[image.Id].Bounds);
            required[image.Root.Id] = Union(required.GetValueOrDefault(image.Root.Id), region);
        }
    }

    private static bool IsPlanar(ShaderImage image) => image.IsAttachment && !image.IsView;
    private static Rect Union(Rect a, Rect b) => a.IsEmpty ? b : b.IsEmpty ? a : a.Union(b);

    internal static PixelRect Scissor(ShaderImageFrame frame, Rect region)
        => PixelRect.FromRect(region.Translate(-frame.RasterBounds.Position), frame.Scale)
            .Intersect(new PixelRect(0, 0, frame.PixelSize.Width, frame.PixelSize.Height));
}
