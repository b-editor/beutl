using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using Beutl.Engine;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Graphics.Effects;

/// <summary>
/// Pixel sort filter effect using GPU-accelerated odd-even transposition sort via GLSL fragment shaders.
/// </summary>
[Display(Name = nameof(Strings.PixelSort), ResourceType = typeof(Strings))]
public sealed partial class PixelSortEffect : FilterEffect
{
    private static readonly ILogger s_logger = Log.CreateLogger<PixelSortEffect>();

    // Prepare shader: encodes sort key into alpha channel.
    // - Anchor pixels (key outside threshold) get alpha=0.0 (sentinel)
    // - Sortable pixels get alpha = key * 0.998 + 0.001  (range: 0.001 ~ 0.999)
    private const string PrepareShaderSource = """
        #version 450

        layout(location = 0) in vec2 fragCoord;
        layout(location = 0) out vec4 outColor;

        layout(set = 0, binding = 0) uniform sampler2D srcTexture;

        layout(push_constant) uniform PushConstants {
            float thresholdMin;
            float thresholdMax;
            int   sortKeyType;
            int   padding;
            float width;
            float height;
        } pc;

        float hue(vec4 c) {
            float cMax = max(c.r, max(c.g, c.b));
            float cMin = min(c.r, min(c.g, c.b));
            float delta = cMax - cMin;
            if (delta < 1e-5) return 0.0;
            float h;
            if (cMax == c.r)      h = mod((c.g - c.b) / delta, 6.0);
            else if (cMax == c.g) h = (c.b - c.r) / delta + 2.0;
            else                  h = (c.r - c.g) / delta + 4.0;
            return h / 6.0;
        }

        float saturation(vec4 c) {
            float cMax = max(c.r, max(c.g, c.b));
            float cMin = min(c.r, min(c.g, c.b));
            return (cMax < 1e-5) ? 0.0 : (cMax - cMin) / cMax;
        }

        float computeKey(vec4 c) {
            if      (pc.sortKeyType == 1) return hue(c);
            else if (pc.sortKeyType == 2) return saturation(c);
            else if (pc.sortKeyType == 3) return c.r;
            else if (pc.sortKeyType == 4) return c.g;
            else if (pc.sortKeyType == 5) return c.b;
            return dot(c.rgb, vec3(0.2126, 0.7152, 0.0722)); // 0 = Luminance
        }

        void main() {
            vec4 color = texture(srcTexture, fragCoord);
            float key = computeKey(color);
            bool isAnchor = (key < pc.thresholdMin || key > pc.thresholdMax);
            float encodedKey = isAnchor ? 0.0 : (key * 0.998 + 0.001);
            outColor = vec4(color.rgb, encodedKey);
        }
        """;

    // Odd-even transposition sort shader: one compare-and-swap pass.
    // Each pass alternates between even phase (pairs 0-1, 2-3, 4-5, ...)
    // and odd phase (pairs 1-2, 3-4, 5-6, ...).
    // Only adjacent pixels are compared, so anchor pixels (alpha ≈ 0) naturally
    // act as segment boundaries - no pixel can be swapped past an anchor.
    private const string OddEvenSortShaderSource = """
        #version 450

        layout(location = 0) in vec2 fragCoord;
        layout(location = 0) out vec4 outColor;

        layout(set = 0, binding = 0) uniform sampler2D srcTexture;

        layout(push_constant) uniform PushConstants {
            int   phase;      // 0 = even phase, 1 = odd phase
            int   sortDir;    // 0 = horizontal, 1 = vertical
            int   ascending;  // 1 = ascending, 0 = descending
            int   padding;
            float width;
            float height;
        } pc;

        void main() {
            ivec2 texSize  = ivec2(int(pc.width), int(pc.height));
            ivec2 texCoord = ivec2(fragCoord * vec2(pc.width, pc.height));

            int idx     = (pc.sortDir == 0) ? texCoord.x : texCoord.y;
            int lineIdx = (pc.sortDir == 0) ? texCoord.y : texCoord.x;
            int maxIdx  = (pc.sortDir == 0) ? texSize.x  : texSize.y;

            // Determine partner based on phase
            // Even phase: pairs (0,1), (2,3), (4,5), ...
            // Odd phase:  pairs (1,2), (3,4), (5,6), ...
            bool isFirstInPair = ((idx - pc.phase) >= 0) && (((idx - pc.phase) & 1) == 0);
            int partnerIdx = isFirstInPair ? (idx + 1) : (idx - 1);

            ivec2 myCoord = (pc.sortDir == 0) ? ivec2(idx, lineIdx) : ivec2(lineIdx, idx);
            vec4 myColor = texelFetch(srcTexture, myCoord, 0);

            // Out-of-bounds partner: keep current pixel
            if (partnerIdx < 0 || partnerIdx >= maxIdx) {
                outColor = myColor;
                return;
            }

            ivec2 partnerCoord = (pc.sortDir == 0)
                ? ivec2(partnerIdx, lineIdx)
                : ivec2(lineIdx, partnerIdx);
            vec4 partnerColor = texelFetch(srcTexture, partnerCoord, 0);

            float myKey      = myColor.a;
            float partnerKey = partnerColor.a;

            // Anchors (alpha ≈ 0) block swaps - this creates natural segment boundaries
            if (myKey < 0.0005 || partnerKey < 0.0005) {
                outColor = myColor;
                return;
            }

            // Compare-and-swap: first element keeps min (ascending) or max (descending)
            bool shouldSwap;
            if (pc.ascending == 1) {
                shouldSwap = isFirstInPair ? (myKey > partnerKey) : (myKey < partnerKey);
            } else {
                shouldSwap = isFirstInPair ? (myKey < partnerKey) : (myKey > partnerKey);
            }

            outColor = shouldSwap ? partnerColor : myColor;
        }
        """;

    // Restore shader: replaces anchor positions with original pixel data.
    // binding 0 = sortedTexture, binding 1 = originalTexture
    private const string RestoreShaderSource = """
        #version 450

        layout(location = 0) in vec2 fragCoord;
        layout(location = 0) out vec4 outColor;

        layout(set = 0, binding = 0) uniform sampler2D sortedTexture;
        layout(set = 0, binding = 1) uniform sampler2D originalTexture;

        layout(push_constant) uniform PushConstants {
            float width;
            float height;
        } pc;

        void main() {
            ivec2 coord   = ivec2(fragCoord * vec2(pc.width, pc.height));
            vec4 sorted   = texelFetch(sortedTexture,   coord, 0);
            vec4 original = texelFetch(originalTexture, coord, 0);

            // sorted.a < 0.0005 means anchor position -> restore original
            outColor = (sorted.a < 0.0005)
                ? original
                : vec4(sorted.rgb, original.a);
        }
        """;

    private static GLSLShader? s_prepareShader;
    private static GLSLShader? s_sortShader;
    private static GLSLShader? s_restoreShader;
    private static bool s_shadersInitialized;

    public PixelSortEffect()
    {
        ScanProperties<PixelSortEffect>();
    }

    [Display(Name = nameof(Strings.SortDirection), ResourceType = typeof(Strings))]
    public IProperty<PixelSortDirection> Direction { get; } = Property.Create(PixelSortDirection.Horizontal);

    [Display(Name = nameof(Strings.SortKey), ResourceType = typeof(Strings))]
    public IProperty<PixelSortKey> SortKey { get; } = Property.Create(PixelSortKey.Luminance);

    [Display(Name = nameof(Strings.ThresholdMin), ResourceType = typeof(Strings))]
    [Range(0f, 1f)]
    public IProperty<float> ThresholdMin { get; } = Property.CreateAnimatable(0.25f);

    [Display(Name = nameof(Strings.ThresholdMax), ResourceType = typeof(Strings))]
    [Range(0f, 1f)]
    public IProperty<float> ThresholdMax { get; } = Property.CreateAnimatable(0.8f);

    [Display(Name = nameof(Strings.Ascending), ResourceType = typeof(Strings))]
    public IProperty<bool> Ascending { get; } = Property.Create(true);

    private static void EnsureShadersInitialized()
    {
        if (s_shadersInitialized) return;
        s_shadersInitialized = true;

        IGraphicsContext? context = GraphicsContextFactory.SharedContext;
        if (context == null || !context.Supports3DRendering)
        {
            s_logger.LogWarning("Vulkan 3D rendering is not available; PixelSort effect will be inactive.");
            return;
        }

        try
        {
            s_prepareShader = GLSLShader.Create(PrepareShaderSource);
            s_sortShader    = GLSLShader.Create(OddEvenSortShaderSource);
            s_restoreShader = GLSLShader.CreateDualTexture(RestoreShaderSource);
        }
        catch (Exception ex)
        {
            s_logger.LogError(ex, "Failed to initialize PixelSort GLSL shaders.");
            s_prepareShader = null;
            s_sortShader    = null;
            s_restoreShader = null;
        }
    }

    private readonly record struct EffectData(
        PixelSortDirection Direction,
        PixelSortKey SortKey,
        float ThresholdMin,
        float ThresholdMax,
        bool Ascending);

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        var data = new EffectData(r.Direction, r.SortKey, r.ThresholdMin, r.ThresholdMax, r.Ascending);
        context.CustomEffect(data, static (d, ctx) => OnApplyTo(d, ctx), static (_, b) => b);
    }

    private static void OnApplyTo(EffectData r, CustomFilterEffectContext ctx)
    {
        EnsureShadersInitialized();

        if (s_prepareShader == null || s_sortShader == null || s_restoreShader == null)
            return;

        IGraphicsContext? gfx = GraphicsContextFactory.SharedContext;
        if (gfx == null || !gfx.Supports3DRendering)
            return;

        for (int i = 0; i < ctx.Targets.Count; i++)
        {
            EffectTarget target = ctx.Targets[i];
            RenderTarget? renderTarget = target.RenderTarget;
            if (renderTarget?.Texture == null) continue;

            ITexture2D originalTexture = renderTarget.Texture;
            int width  = originalTexture.Width;
            int height = originalTexture.Height;

            // Odd-even transposition sort needs N iterations for N elements (worst case).
            // For pixel sorting, the effective iteration count is bounded by the
            // longest segment between anchors, which is typically much smaller than N.
            int sortLen = r.Direction == PixelSortDirection.Horizontal ? width : height;

            ITexture2D pingTexture = gfx.CreateTexture2D(width, height, TextureFormat.BGRA8Unorm);
            ITexture2D pongTexture = gfx.CreateTexture2D(width, height, TextureFormat.BGRA8Unorm);

            try
            {
                using ITexture2D depthTexture = gfx.CreateTexture2D(width, height, TextureFormat.Depth32Float);

                // Pass 1: Prepare - encode sort key into alpha
                s_prepareShader.ExecuteSingleTarget(
                    originalTexture, pingTexture, depthTexture,
                    new PreparePushConstants
                    {
                        ThresholdMin = r.ThresholdMin,
                        ThresholdMax = r.ThresholdMax,
                        SortKeyType  = (int)r.SortKey,
                        Padding      = 0,
                        Width        = width,
                        Height       = height,
                    });

                // Passes 2..N: Odd-even transposition sort with ping-pong
                // Each iteration runs one even phase + one odd phase = 2 draw calls.
                // sortLen/2 iterations is sufficient for a full sort.
                ITexture2D current = pingTexture;
                ITexture2D next    = pongTexture;
                int iterations = (sortLen + 1) / 2;

                for (int iter = 0; iter < iterations; iter++)
                {
                    // Even phase (phase = 0)
                    s_sortShader.ExecuteSingleTarget(
                        current, next, depthTexture,
                        new SortPushConstants
                        {
                            Phase     = 0,
                            SortDir   = (int)r.Direction,
                            Ascending = r.Ascending ? 1 : 0,
                            Padding   = 0,
                            Width     = width,
                            Height    = height,
                        });
                    (current, next) = (next, current);

                    // Odd phase (phase = 1)
                    s_sortShader.ExecuteSingleTarget(
                        current, next, depthTexture,
                        new SortPushConstants
                        {
                            Phase     = 1,
                            SortDir   = (int)r.Direction,
                            Ascending = r.Ascending ? 1 : 0,
                            Padding   = 0,
                            Width     = width,
                            Height    = height,
                        });
                    (current, next) = (next, current);
                }

                // Final pass: Restore anchor pixels from original image
                EffectTarget newTarget = ctx.CreateTarget(target.Bounds);
                RenderTarget? newRenderTarget = newTarget.RenderTarget;

                if (newRenderTarget?.Texture == null)
                {
                    newTarget.Dispose();
                    continue;
                }

                s_restoreShader.ExecuteSingleTargetWithMask(
                    current, originalTexture, newRenderTarget.Texture, depthTexture,
                    new RestorePushConstants
                    {
                        Width  = width,
                        Height = height,
                    });

                target.Dispose();
                ctx.Targets[i] = newTarget;
            }
            finally
            {
                pingTexture.Dispose();
                pongTexture.Dispose();
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PreparePushConstants
    {
        public float ThresholdMin;
        public float ThresholdMax;
        public int   SortKeyType;
        public int   Padding;
        public float Width;
        public float Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SortPushConstants
    {
        public int   Phase;
        public int   SortDir;
        public int   Ascending;
        public int   Padding;
        public float Width;
        public float Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RestorePushConstants
    {
        public float Width;
        public float Height;
    }
}
