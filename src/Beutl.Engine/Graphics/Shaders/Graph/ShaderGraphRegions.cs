using System.Collections.Immutable;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.Graphics.Shaders;

/// <summary>One request's coordinate frames and conservative per-pass evaluation rectangles.</summary>
internal sealed class ShaderGraphRegions
{
    public Dictionary<int, ShaderImageFrame> Frames { get; } = [];
    public Rect?[] PassRegions { get; }
    public Rect SourceRegion { get; }

    /// <param name="requiredOutput">The part of the output the request needs. Empty when a plan does not know that yet,
    /// which leaves images that can hold part of their bounds with next to nothing.</param>
    /// <param name="externalFrames">Where a render's other pictures were rasterized; left out, as the source was.</param>
    public ShaderGraphRegions(ShaderGraphDescription graph, ShaderImageFrame source, float scale,
        Vector gridOffset, Rect requiredOutput, IReadOnlyList<ShaderImageFrame>? externalFrames = null)
    {
        Frames.Add(0, source);
        foreach (ShaderImage image in graph.Resources.OfType<ShaderImage>())
        {
            if (image.Id == 0 || graph.LastUses[image.Id] < 0) continue;
            if (image.ExternalIndex >= 0)
            {
                Frames.Add(image.Id, externalFrames?[image.ExternalIndex] ?? source);
                continue;
            }
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
        if (requiredOutput.Width > 0 && requiredOutput.Height > 0)
            Require(selected, requiredOutput.Inflate(2 / Frames[selected.Id].Scale));
        for (int index = graph.Passes.Length - 1; index >= 0; index--)
        {
            ShaderPassDescription pass = graph.Passes[index];
            bool local = pass.IsLocal;
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
                if (local && (pass.InputRegions.ContainsKey(binding.Binding) || pass.OriginInputRegions.ContainsKey(binding.Binding))
                    && binding.Sampler.AddressU is SamplerAddressMode.ClampToEdge or SamplerAddressMode.ClampToBorder
                    && binding.Sampler.AddressV is SamplerAddressMode.ClampToEdge or SamplerAddressMode.ClampToBorder)
                {
                    if (output.IsEmpty) continue;
                    input = MapInput(pass, binding.Binding, output);
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
        foreach (ImmutableArray<ShaderImage> images in graph.PartialImageSets)
            FitToRequired(images, required, gridOffset);

        void Require(ShaderImage image, Rect region)
        {
            region = region.Intersect(Frames[image.Id].Bounds);
            required[image.Root.Id] = Union(required.GetValueOrDefault(image.Root.Id), region);
        }
    }

    private Rect MapInput(ShaderPassDescription pass, int binding, Rect output)
    {
        if (pass.InputRegions.TryGetValue(binding, out ShaderInputRegion contract))
            return contract.Map(output);
        // The origin a pass's constants receive, so that what is required is what the pass reads.
        return pass.OriginInputRegions[binding].Map(output, Frames[0]);
    }

    private void FitToRequired(ImmutableArray<ShaderImage> images, Dictionary<int, Rect> required, Vector gridOffset)
    {
        ShaderImageFrame frame = Frames[images[0].Id];
        // A pass refuses attachments whose frames differ, so these stay as they are for it to report.
        if (images.Any(image => Frames[image.Id] != frame)) return;
        Rect region = images.Aggregate(Rect.Empty, (all, image) => Union(all, required.GetValueOrDefault(image.Id)));
        PixelRect whole = PixelRect.FromRect(frame.Bounds.Translate(gridOffset), frame.Scale);
        PixelRect needed = PixelRect.FromRect(region.Translate(gridOffset), frame.Scale).Intersect(whole);
        PixelRect pixels = needed.Width > 0 && needed.Height > 0
            ? Window(needed, whole) : new PixelRect(whole.X, whole.Y, 1, 1);
        if (pixels == whole) return;
        ShaderImageFrame fitted = ShaderImageFrame.FromPixels(frame.Bounds, pixels, frame.Scale, gridOffset);
        foreach (ShaderImage image in images)
            Frames[image.Id] = fitted;
    }

    // An execution takes a retained image of the very size it asks for, so a region that moves or grows a little has to
    // keep asking for the same size.
    private static PixelRect Window(PixelRect needed, PixelRect whole)
    {
        const int step = 256;
        int width = Math.Min((needed.Width + step - 1) / step * step, whole.Width);
        int height = Math.Min((needed.Height + step - 1) / step * step, whole.Height);
        return new(Math.Min(needed.X, whole.Right - width), Math.Min(needed.Y, whole.Bottom - height), width, height);
    }

    private static Rect Union(Rect a, Rect b) => a.IsEmpty ? b : b.IsEmpty ? a : a.Union(b);

    internal static PixelRect Scissor(ShaderImageFrame frame, Rect region)
        => PixelRect.FromRect(region.Translate(-frame.RasterBounds.Position), frame.Scale)
            .Intersect(new PixelRect(0, 0, frame.PixelSize.Width, frame.PixelSize.Height));
}
