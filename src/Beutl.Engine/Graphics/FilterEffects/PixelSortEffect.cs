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
[Display(Name = nameof(GraphicsStrings.PixelSortEffect), ResourceType = typeof(GraphicsStrings))]
public sealed partial class PixelSortEffect : FilterEffect
{
    private static readonly ILogger s_logger = Log.CreateLogger<PixelSortEffect>();

    private const string PrepareShaderSource = """
        #version 450

        layout(location = 0) in vec2 fragCoord;
        layout(location = 0) out vec4 outColor;

        layout(set = 0, binding = 0) uniform sampler2D srcTexture;
        layout(constant_id = 0) const int sortKeyType = 0;

        layout(push_constant) uniform PushConstants {
            float thresholdMin;
            float thresholdMax;
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
            if      (sortKeyType == 1) return hue(c);
            else if (sortKeyType == 2) return saturation(c);
            else if (sortKeyType == 3) return c.r;
            else if (sortKeyType == 4) return c.g;
            else if (sortKeyType == 5) return c.b;
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
        layout(constant_id = 0) const int sortDir = 0;

        layout(push_constant) uniform PushConstants {
            float width;
            float height;
        } pc;

        void main() {
            ivec2 coord = ivec2(fragCoord * vec2(pc.width, pc.height));
            int idx     = (sortDir == 0) ? coord.x : coord.y;
            int lineIdx = (sortDir == 0) ? coord.y : coord.x;
            int maxIdx  = (sortDir == 0) ? int(pc.width) : int(pc.height);

            float myKey = texelFetch(srcTexture, coord, 0).a;

            // Anchor → output zero marker
            if (myKey < 0.0005) {
                outColor = vec4(0.0);
                return;
            }

            // Find segment start
            int segStart = idx;
            for (int s = idx - 1; s >= 0; s--) {
                ivec2 c = (sortDir == 0) ? ivec2(s, lineIdx) : ivec2(lineIdx, s);
                if (texelFetch(srcTexture, c, 0).a < 0.0005) break;
                segStart = s;
            }

            // Find segment end
            int segEnd = idx;
            for (int s = idx + 1; s < maxIdx; s++) {
                ivec2 c = (sortDir == 0) ? ivec2(s, lineIdx) : ivec2(lineIdx, s);
                if (texelFetch(srcTexture, c, 0).a < 0.0005) break;
                segEnd = s;
            }

            // Compute rank: count elements with strictly smaller key,
            // or same key but lower index (stable sort)
            int rank = 0;
            for (int j = segStart; j <= segEnd; j++) {
                if (j == idx) continue;
                ivec2 c = (sortDir == 0) ? ivec2(j, lineIdx) : ivec2(lineIdx, j);
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
        layout(constant_id = 0) const int sortDir = 0;
        layout(constant_id = 1) const int ascending = 1;

        layout(push_constant) uniform PushConstants {
            float width;
            float height;
        } pc;

        void main() {
            ivec2 coord = ivec2(fragCoord * vec2(pc.width, pc.height));
            int idx     = (sortDir == 0) ? coord.x : coord.y;
            int lineIdx = (sortDir == 0) ? coord.y : coord.x;
            int maxIdx  = (sortDir == 0) ? int(pc.width) : int(pc.height);

            vec4 rankData = texelFetch(rankTexture, coord, 0);

            // Anchor → output original
            if (rankData.b < 0.5) {
                outColor = texelFetch(originalTexture, coord, 0);
                return;
            }

            // Find segment boundaries using B channel
            int segStart = idx;
            for (int s = idx - 1; s >= 0; s--) {
                ivec2 c = (sortDir == 0) ? ivec2(s, lineIdx) : ivec2(lineIdx, s);
                if (texelFetch(rankTexture, c, 0).b < 0.5) break;
                segStart = s;
            }

            int segEnd = idx;
            for (int s = idx + 1; s < maxIdx; s++) {
                ivec2 c = (sortDir == 0) ? ivec2(s, lineIdx) : ivec2(lineIdx, s);
                if (texelFetch(rankTexture, c, 0).b < 0.5) break;
                segEnd = s;
            }

            // Target rank for this output position
            int targetRank = (ascending == 1)
                ? (idx - segStart)
                : (segEnd - idx);

            // Find the element whose rank == targetRank
            vec4 originalAtIdx = texelFetch(originalTexture, coord, 0);

            for (int j = segStart; j <= segEnd; j++) {
                ivec2 cj = (sortDir == 0) ? ivec2(j, lineIdx) : ivec2(lineIdx, j);
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

    // These fixed slots cover the complete finite specialization domain: six prepare, two rank,
    // and four gather pipelines. They are retained for the process lifetime and never grow or evict.
    private static readonly PixelSortPipelineCache<GLSLShader> s_shaderCache = new(
        static sortKey => GLSLShader.CreateBuiltIn(
            PrepareShaderSource,
            [SpecializationConstant.Create(0, (int)sortKey, ShaderStage.Fragment)]),
        static direction => GLSLShader.CreateBuiltIn(
            RankShaderSource,
            [SpecializationConstant.Create(0, (int)direction, ShaderStage.Fragment)]),
        static (direction, ascending) => GLSLShader.CreateBuiltIn(
            GatherRestoreShaderSource,
            [
                SpecializationConstant.Create(0, (int)direction, ShaderStage.Fragment),
                SpecializationConstant.Create(1, ascending ? 1 : 0, ShaderStage.Fragment),
            ],
            hasMaskTexture: true));

    public PixelSortEffect()
    {
        ScanProperties<PixelSortEffect>();
    }

    [Display(Name = nameof(GraphicsStrings.PixelSortEffect_Direction), ResourceType = typeof(GraphicsStrings))]
    public IProperty<PixelSortDirection> Direction { get; } = Property.Create(PixelSortDirection.Horizontal);

    [Display(Name = nameof(GraphicsStrings.PixelSortEffect_SortKey), ResourceType = typeof(GraphicsStrings))]
    public IProperty<PixelSortKey> SortKey { get; } = Property.Create(PixelSortKey.Luminance);

    [Display(Name = nameof(GraphicsStrings.PixelSortEffect_ThresholdMin), ResourceType = typeof(GraphicsStrings))]
    [Range(0f, 100f)]
    public IProperty<float> ThresholdMin { get; } = Property.CreateAnimatable(25f);

    [Display(Name = nameof(GraphicsStrings.PixelSortEffect_ThresholdMax), ResourceType = typeof(GraphicsStrings))]
    [Range(0f, 100f)]
    public IProperty<float> ThresholdMax { get; } = Property.CreateAnimatable(80f);

    [Display(Name = nameof(GraphicsStrings.PixelSortEffect_Ascending), ResourceType = typeof(GraphicsStrings))]
    public IProperty<bool> Ascending { get; } = Property.Create(true);

    private static PixelSortPipelines<GLSLShader>? GetOrCreateShaders(
        PixelSortDirection direction,
        PixelSortKey sortKey,
        bool ascending)
    {
        IGraphicsContext? context = GraphicsContextFactory.SharedContext;
        if (context == null || !context.Supports3DRendering)
        {
            s_logger.LogWarning("Vulkan 3D rendering is not available; PixelSort effect will be inactive.");
            return null;
        }

        try
        {
            return s_shaderCache.GetOrCreate(sortKey, direction, ascending);
        }
        catch (Exception ex)
        {
            s_logger.LogError(ex, "Failed to initialize a PixelSort GLSL shader variant.");
            return null;
        }
    }

    // Delivery must not ship silently unsorted frames; preview keeps the source pixels and logs.
    // Cancellation always propagates.
    internal static bool ShouldRethrowPassFailure(Exception exception, RenderIntent intent)
        => exception is OperationCanceledException || intent == RenderIntent.Delivery;

    internal static void ThrowIfDeliveryAllocationFailure(RenderIntent intent, int targetIndex)
    {
        if (intent == RenderIntent.Delivery)
        {
            throw new InvalidOperationException(
                $"PixelSort output target {targetIndex} has no GPU texture; the delivery render fails instead of shipping unsorted pixels.");
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
        PixelSortPipelines<GLSLShader>? shaderPipelines = GetOrCreateShaders(
            r.Direction,
            r.SortKey,
            r.Ascending);
        if (shaderPipelines is not { } shaders)
            return;

        IGraphicsContext? gfx = GraphicsContextFactory.SharedContext;
        if (gfx == null || !gfx.Supports3DRendering)
            return;

        for (int i = 0; i < ctx.Targets.Count; i++)
        {
            EffectTarget target = ctx.Targets[i];
            RenderTarget? renderTarget = target.RenderTarget;
            if (renderTarget?.Texture == null) continue;

            // These passes read the backing texture from a separate Vulkan submission, which Skia's
            // own ordering does not cover: an unsubmitted source reads back empty, and an empty
            // source makes every pixel an anchor, so the gather pass returns the unsorted image.
            renderTarget.PrepareForSampling(RenderTargetSamplingIntent.BackendInterop);

            ITexture2D originalTexture = renderTarget.Texture;
            int width = originalTexture.Width;
            int height = originalTexture.Height;

            try
            {
                using NativeFilterTextureLease prepLease = ctx.AcquireNativeScratchTexture(
                    gfx,
                    width,
                    height);
                using NativeFilterTextureLease rankLease = ctx.AcquireNativeScratchTexture(
                    gfx,
                    width,
                    height);
                ITexture2D prepTexture = prepLease.Texture;
                ITexture2D rankTexture = rankLease.Texture;

                // Pass 1: Prepare - encode sort key into alpha
                shaders.Prepare.ExecuteSingleTarget(
                    originalTexture, prepTexture,
                    new PreparePushConstants
                    {
                        ThresholdMin = r.ThresholdMin,
                        ThresholdMax = r.ThresholdMax,
                        Width = width,
                        Height = height,
                    });

                // Pass 2: Rank - compute each pixel's rank within its segment
                shaders.Rank.ExecuteSingleTarget(
                    prepTexture, rankTexture,
                    new RankPushConstants
                    {
                        Width = width,
                        Height = height,
                    });

                // Pass 3: Gather + Restore - place pixels by rank, restore anchors
                EffectTarget newTarget = ctx.CreateNativeTargetLike(target);
                RenderTarget? newRenderTarget = newTarget.RenderTarget;

                if (newRenderTarget is null)
                {
                    newTarget.Dispose();
                    continue;
                }

                if (newRenderTarget.Texture is null)
                {
                    newTarget.Dispose();
                    ThrowIfDeliveryAllocationFailure(ctx.Intent, i);
                    ctx.RenderTargetLeaseSession?.MarkContentDropped();
                    continue;
                }

                try
                {
                    shaders.Gather.ExecuteSingleTargetWithMask(
                        rankTexture, originalTexture, newRenderTarget.Texture,
                        new GatherPushConstants
                        {
                            Width = width,
                            Height = height,
                        });
                    shaders.Gather.SubmitPendingCommands();

                    target.Dispose();
                    ctx.Targets[i] = newTarget;
                }
                catch (Exception ex)
                {
                    newTarget.Dispose();
                    if (ShouldRethrowPassFailure(ex, ctx.Intent))
                    {
                        throw;
                    }

                    s_logger.LogWarning(ex, "PixelSort gather pass failed for target {Index}; keeping the source pixels.", i);
                }
            }
            catch (Exception ex)
            {
                if (ShouldRethrowPassFailure(ex, ctx.Intent))
                {
                    throw;
                }

                s_logger.LogWarning(ex, "PixelSort pass failed for target {Index}; leaving it unsorted.", i);
                continue;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PreparePushConstants
    {
        public float ThresholdMin;
        public float ThresholdMax;
        public float Width;
        public float Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RankPushConstants
    {
        public float Width;
        public float Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GatherPushConstants
    {
        public float Width;
        public float Height;
    }
}

internal readonly record struct PixelSortPipelines<TPipeline>(
    TPipeline Prepare,
    TPipeline Rank,
    TPipeline Gather)
    where TPipeline : class;

internal sealed class PixelSortPipelineCache<TPipeline>
    where TPipeline : class
{
    private readonly object _sync = new();
    private readonly Func<PixelSortKey, TPipeline> _createPrepare;
    private readonly Func<PixelSortDirection, TPipeline> _createRank;
    private readonly Func<PixelSortDirection, bool, TPipeline> _createGather;
    private readonly Slot[] _prepareSlots = new Slot[6];
    private readonly Slot[] _rankSlots = new Slot[2];
    private readonly Slot[] _gatherSlots = new Slot[4];

    public PixelSortPipelineCache(
        Func<PixelSortKey, TPipeline> createPrepare,
        Func<PixelSortDirection, TPipeline> createRank,
        Func<PixelSortDirection, bool, TPipeline> createGather)
    {
        _createPrepare = createPrepare;
        _createRank = createRank;
        _createGather = createGather;
    }

    public PixelSortPipelines<TPipeline>? GetOrCreate(
        PixelSortKey sortKey,
        PixelSortDirection direction,
        bool ascending)
    {
        int prepareIndex = GetSortKeyIndex(sortKey);
        int rankIndex = GetDirectionIndex(direction);
        int gatherIndex = (rankIndex * 2) + (ascending ? 1 : 0);

        lock (_sync)
        {
            ref Slot prepareSlot = ref _prepareSlots[prepareIndex];
            // Publish a slot only after its factory succeeds. Pipeline creation can fail for transient
            // device or resource reasons that are indistinguishable here from deterministic validation
            // failures, so an exception deliberately leaves the slot empty for the next invocation to retry.
            prepareSlot.Value ??= _createPrepare(sortKey);

            if (prepareSlot.Value is not { } prepare)
                return null;

            ref Slot rankSlot = ref _rankSlots[rankIndex];
            rankSlot.Value ??= _createRank(direction);

            if (rankSlot.Value is not { } rank)
                return null;

            ref Slot gatherSlot = ref _gatherSlots[gatherIndex];
            gatherSlot.Value ??= _createGather(direction, ascending);

            return gatherSlot.Value is { } gather
                ? new PixelSortPipelines<TPipeline>(prepare, rank, gather)
                : null;
        }
    }

    private static int GetDirectionIndex(PixelSortDirection direction)
        => direction switch
        {
            PixelSortDirection.Horizontal => 0,
            PixelSortDirection.Vertical => 1,
            _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null),
        };

    private static int GetSortKeyIndex(PixelSortKey sortKey)
        => sortKey switch
        {
            PixelSortKey.Luminance => 0,
            PixelSortKey.Hue => 1,
            PixelSortKey.Saturation => 2,
            PixelSortKey.Red => 3,
            PixelSortKey.Green => 4,
            PixelSortKey.Blue => 5,
            _ => throw new ArgumentOutOfRangeException(nameof(sortKey), sortKey, null),
        };

    private struct Slot
    {
        public TPipeline? Value;
    }
}
