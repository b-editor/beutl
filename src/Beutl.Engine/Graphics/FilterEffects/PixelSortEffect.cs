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
/// Pixel sort filter effect using rank-based gather sort via GLSL fragment shaders.
/// Only 3 draw calls regardless of image size: Prepare → Rank → Gather+Restore.
/// Each pixel computes its rank within its segment in O(L) where L is segment length.
/// </summary>
[Display(Name = nameof(Strings.PixelSort), ResourceType = typeof(Strings))]
public sealed partial class PixelSortEffect : FilterEffect
{
    private static readonly ILogger s_logger = Log.CreateLogger<PixelSortEffect>();

    private const string PrepareShaderSource = """
        #version 450

        layout(location = 0) in vec2 fragCoord;
        layout(location = 0) out vec4 outColor;

        layout(set = 0, binding = 0) uniform sampler2D srcTexture;

        layout(push_constant) uniform PushConstants {
            float thresholdMin;
            float thresholdMax;
            int   sortKeyType;
            int   sortDir;
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
            return dot(c.rgb, vec3(0.2126, 0.7152, 0.0722));
        }

        void main() {
            ivec2 coord = ivec2(fragCoord * vec2(pc.width, pc.height));
            vec4 color = texelFetch(srcTexture, coord, 0);
            float key = computeKey(color);
            bool isAnchor = (key < pc.thresholdMin || key > pc.thresholdMax);
            float encodedKey = isAnchor ? 0.0 : max(1.0 / 255.0, key * 0.998 + 0.001);
            outColor = vec4(color.rgb, encodedKey);
        }
        """;

    private const string RankShaderSource = """
        #version 450

        layout(location = 0) in vec2 fragCoord;
        layout(location = 0) out vec4 outColor;

        layout(set = 0, binding = 0) uniform sampler2D srcTexture;

        layout(push_constant) uniform PushConstants {
            int   sortDir;
            float width;
            float height;
        } pc;

        void main() {
            ivec2 coord = ivec2(fragCoord * vec2(pc.width, pc.height));
            int idx     = (pc.sortDir == 0) ? coord.x : coord.y;
            int lineIdx = (pc.sortDir == 0) ? coord.y : coord.x;
            int maxIdx  = (pc.sortDir == 0) ? int(pc.width) : int(pc.height);

            float myKey = texelFetch(srcTexture, coord, 0).a;

            // Anchor → output zero marker
            if (myKey < 0.0005) {
                outColor = vec4(0.0);
                return;
            }

            // Find segment start
            int segStart = idx;
            for (int s = idx - 1; s >= 0; s--) {
                ivec2 c = (pc.sortDir == 0) ? ivec2(s, lineIdx) : ivec2(lineIdx, s);
                if (texelFetch(srcTexture, c, 0).a < 0.0005) break;
                segStart = s;
            }

            // Find segment end
            int segEnd = idx;
            for (int s = idx + 1; s < maxIdx; s++) {
                ivec2 c = (pc.sortDir == 0) ? ivec2(s, lineIdx) : ivec2(lineIdx, s);
                if (texelFetch(srcTexture, c, 0).a < 0.0005) break;
                segEnd = s;
            }

            // Compute rank: count elements with strictly smaller key,
            // or same key but lower index (stable sort)
            int rank = 0;
            for (int j = segStart; j <= segEnd; j++) {
                if (j == idx) continue;
                ivec2 c = (pc.sortDir == 0) ? ivec2(j, lineIdx) : ivec2(lineIdx, j);
                float otherKey = texelFetch(srcTexture, c, 0).a;
                if (otherKey < myKey || (otherKey == myKey && j < idx)) {
                    rank++;
                }
            }

            // Encode: R = rank low byte, G = rank high byte, B = 1.0 (sortable marker)
            outColor = vec4(
                float(rank & 255) / 255.0,
                float((rank >> 8) & 255) / 255.0,
                1.0,
                0.0
            );
        }
        """;

    private const string GatherRestoreShaderSource = """
        #version 450

        layout(location = 0) in vec2 fragCoord;
        layout(location = 0) out vec4 outColor;

        layout(set = 0, binding = 0) uniform sampler2D rankTexture;
        layout(set = 0, binding = 1) uniform sampler2D originalTexture;

        layout(push_constant) uniform PushConstants {
            int   sortDir;
            int   ascending;
            float width;
            float height;
        } pc;

        void main() {
            ivec2 coord = ivec2(fragCoord * vec2(pc.width, pc.height));
            int idx     = (pc.sortDir == 0) ? coord.x : coord.y;
            int lineIdx = (pc.sortDir == 0) ? coord.y : coord.x;
            int maxIdx  = (pc.sortDir == 0) ? int(pc.width) : int(pc.height);

            vec4 rankData = texelFetch(rankTexture, coord, 0);

            // Anchor → output original
            if (rankData.b < 0.5) {
                outColor = texelFetch(originalTexture, coord, 0);
                return;
            }

            // Find segment boundaries using B channel
            int segStart = idx;
            for (int s = idx - 1; s >= 0; s--) {
                ivec2 c = (pc.sortDir == 0) ? ivec2(s, lineIdx) : ivec2(lineIdx, s);
                if (texelFetch(rankTexture, c, 0).b < 0.5) break;
                segStart = s;
            }

            int segEnd = idx;
            for (int s = idx + 1; s < maxIdx; s++) {
                ivec2 c = (pc.sortDir == 0) ? ivec2(s, lineIdx) : ivec2(lineIdx, s);
                if (texelFetch(rankTexture, c, 0).b < 0.5) break;
                segEnd = s;
            }

            // Target rank for this output position
            int targetRank = (pc.ascending == 1)
                ? (idx - segStart)
                : (segEnd - idx);

            // Find the element whose rank == targetRank
            vec4 originalAtIdx = texelFetch(originalTexture, coord, 0);

            for (int j = segStart; j <= segEnd; j++) {
                ivec2 cj = (pc.sortDir == 0) ? ivec2(j, lineIdx) : ivec2(lineIdx, j);
                vec4 rd = texelFetch(rankTexture, cj, 0);
                int rank = int(rd.r * 255.0 + 0.5) + int(rd.g * 255.0 + 0.5) * 256;

                if (rank == targetRank) {
                    vec4 srcColor = texelFetch(originalTexture, cj, 0);
                    outColor = vec4(srcColor.rgb, originalAtIdx.a);
                    return;
                }
            }

            // Fallback
            outColor = originalAtIdx;
        }
        """;

    private static GLSLShader? s_prepareShader;
    private static GLSLShader? s_rankShader;
    private static GLSLShader? s_gatherShader;
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
    [Range(0f, 100f)]
    public IProperty<float> ThresholdMin { get; } = Property.CreateAnimatable(25f);

    [Display(Name = nameof(Strings.ThresholdMax), ResourceType = typeof(Strings))]
    [Range(0f, 100f)]
    public IProperty<float> ThresholdMax { get; } = Property.CreateAnimatable(80f);

    [Display(Name = nameof(Strings.Ascending), ResourceType = typeof(Strings))]
    public IProperty<bool> Ascending { get; } = Property.Create(true);

    private static void EnsureShadersInitialized()
    {
        if (s_shadersInitialized) return;

        IGraphicsContext? context = GraphicsContextFactory.SharedContext;
        if (context == null || !context.Supports3DRendering)
        {
            s_logger.LogWarning("Vulkan 3D rendering is not available; PixelSort effect will be inactive.");
            return;
        }

        try
        {
            s_prepareShader = GLSLShader.Create(PrepareShaderSource);
            s_rankShader = GLSLShader.Create(RankShaderSource);
            s_gatherShader = GLSLShader.CreateDualTexture(GatherRestoreShaderSource);
            s_shadersInitialized = true;
        }
        catch (Exception ex)
        {
            s_logger.LogError(ex, "Failed to initialize PixelSort GLSL shaders.");
            s_prepareShader = null;
            s_rankShader = null;
            s_gatherShader = null;
            s_shadersInitialized = true;
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
        var data = new EffectData(r.Direction, r.SortKey, r.ThresholdMin / 100f, r.ThresholdMax / 100f, r.Ascending);
        context.CustomEffect(data, static (d, ctx) => OnApplyTo(d, ctx), static (_, b) => b);
    }

    private static void OnApplyTo(EffectData r, CustomFilterEffectContext ctx)
    {
        EnsureShadersInitialized();

        if (s_prepareShader == null || s_rankShader == null || s_gatherShader == null)
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
            int width = originalTexture.Width;
            int height = originalTexture.Height;

            using ITexture2D prepTexture = gfx.CreateTexture2D(width, height, TextureFormat.BGRA8Unorm);
            using ITexture2D rankTexture = gfx.CreateTexture2D(width, height, TextureFormat.BGRA8Unorm);
            using ITexture2D depth = gfx.CreateTexture2D(width, height, TextureFormat.Depth32Float);

            // Pass 1: Prepare - encode sort key into alpha
            s_prepareShader.ExecuteSingleTarget(
                originalTexture, prepTexture, depth,
                new PreparePushConstants
                {
                    ThresholdMin = r.ThresholdMin,
                    ThresholdMax = r.ThresholdMax,
                    SortKeyType = (int)r.SortKey,
                    SortDir = (int)r.Direction,
                    Width = width,
                    Height = height,
                });

            // Pass 2: Rank - compute each pixel's rank within its segment
            s_rankShader.ExecuteSingleTarget(
                prepTexture, rankTexture, depth,
                new RankPushConstants
                {
                    SortDir = (int)r.Direction,
                    Width = width,
                    Height = height,
                });

            // Pass 3: Gather + Restore - place pixels by rank, restore anchors
            EffectTarget newTarget = ctx.CreateTarget(target.Bounds);
            RenderTarget? newRenderTarget = newTarget.RenderTarget;

            if (newRenderTarget?.Texture == null)
            {
                newTarget.Dispose();
                continue;
            }

            try
            {
                using ITexture2D gatherDepth = gfx.CreateTexture2D(width, height, TextureFormat.Depth32Float);

                s_gatherShader.ExecuteSingleTargetWithMask(
                    rankTexture, originalTexture, newRenderTarget.Texture, gatherDepth,
                    new GatherPushConstants
                    {
                        SortDir = (int)r.Direction,
                        Ascending = r.Ascending ? 1 : 0,
                        Width = width,
                        Height = height,
                    });

                target.Dispose();
                ctx.Targets[i] = newTarget;
            }
            catch
            {
                newTarget.Dispose();
                throw;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PreparePushConstants
    {
        public float ThresholdMin;
        public float ThresholdMax;
        public int SortKeyType;
        public int SortDir;
        public float Width;
        public float Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RankPushConstants
    {
        public int SortDir;
        public float Width;
        public float Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GatherPushConstants
    {
        public int SortDir;
        public int Ascending;
        public float Width;
        public float Height;
    }
}
