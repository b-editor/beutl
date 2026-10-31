using System.ComponentModel.DataAnnotations;
using System.Numerics;
using Beutl.Engine;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Shaders;
using Beutl.Language;

namespace Beutl.Graphics.Effects;

/// <summary>
/// Pixel sort filter effect using rank-based gather sort via GLSL fragment shaders.
/// Three sorting passes regardless of image size: Prepare → Rank → Gather+Restore.
/// Each pixel computes its rank within its segment in O(L) where L is segment length.
/// </summary>
[Display(Name = nameof(GraphicsStrings.PixelSortEffect), ResourceType = typeof(GraphicsStrings))]
public sealed partial class PixelSortEffect : FilterEffect
{
    private const string PrepareShaderSource = """
        #version 450

        layout(location = 0) in vec2 fragCoord;
        layout(location = 0) out vec4 outColor;

        layout(set = 0, binding = 0) uniform sampler2D srcTexture;
        layout(constant_id = 0) const int sortKeyType = 0;

        layout(push_constant) uniform PushConstants {
            float thresholdMin;
            float thresholdMax;
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
            ivec2 coord = ivec2(gl_FragCoord.xy);
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

        void main() {
            ivec2 coord = ivec2(gl_FragCoord.xy);
            int idx     = (sortDir == 0) ? coord.x : coord.y;
            int lineIdx = (sortDir == 0) ? coord.y : coord.x;
            int maxIdx  = (sortDir == 0) ? textureSize(srcTexture, 0).x : textureSize(srcTexture, 0).y;

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

        void main() {
            ivec2 coord = ivec2(gl_FragCoord.xy);
            int idx     = (sortDir == 0) ? coord.x : coord.y;
            int lineIdx = (sortDir == 0) ? coord.y : coord.x;
            int maxIdx  = (sortDir == 0) ? textureSize(rankTexture, 0).x : textureSize(rankTexture, 0).y;

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

    // Only immutable definitions are retained here. Device pipelines belong to the renderer's cache.
    // Indexed by sort key.
    private static readonly ShaderProgram[] s_preparePrograms = [.. Enumerable.Range(0, 6).Select(static key =>
        ShaderProgram.Fullscreen(new ShaderSource(PrepareShaderSource))
            .Specialize([SpecializationConstant.Create(0, key, ShaderStage.Fragment)]))];

    // Indexed by direction.
    private static readonly ShaderProgram[] s_rankPrograms = [.. Enumerable.Range(0, 2).Select(static direction =>
        ShaderProgram.Fullscreen(new ShaderSource(RankShaderSource))
            .Specialize([SpecializationConstant.Create(0, direction, ShaderStage.Fragment)]))];

    // Indexed by direction * 2 + (ascending ? 1 : 0).
    private static readonly ShaderProgram[] s_gatherPrograms = [.. Enumerable.Range(0, 4).Select(static index =>
        ShaderProgram.Fullscreen(new ShaderSource(GatherRestoreShaderSource))
            .Specialize([
                SpecializationConstant.Create(0, index / 2, ShaderStage.Fragment),
                SpecializationConstant.Create(1, index % 2, ShaderStage.Fragment)
            ]))];

    public PixelSortEffect()
    {
        ScanProperties<PixelSortEffect>();
    }

    [Display(Name = nameof(GraphicsStrings.PixelSortEffect_Direction), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<PixelSortDirection> Direction { get; } = Property.CreateExpression(PixelSortDirection.Horizontal);

    [Display(Name = nameof(GraphicsStrings.PixelSortEffect_SortKey), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<PixelSortKey> SortKey { get; } = Property.CreateExpression(PixelSortKey.Luminance);

    [Display(Name = nameof(GraphicsStrings.PixelSortEffect_ThresholdMin), ResourceType = typeof(GraphicsStrings))]
    [Range(0f, 100f)]
    public IExpressionProperty<float> ThresholdMin { get; } = Property.CreateAnimatable(25f);

    [Display(Name = nameof(GraphicsStrings.PixelSortEffect_ThresholdMax), ResourceType = typeof(GraphicsStrings))]
    [Range(0f, 100f)]
    public IExpressionProperty<float> ThresholdMax { get; } = Property.CreateAnimatable(80f);

    [Display(Name = nameof(GraphicsStrings.PixelSortEffect_Ascending), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<bool> Ascending { get; } = Property.CreateExpression(true);

    public new partial class Resource
    {
        protected override void ApplyTo(FilterEffectContext context)
        {
            int sortKey = GetSortKeyIndex(SortKey);
            int direction = GetDirectionIndex(Direction);
            var programs = (Prepare: s_preparePrograms[sortKey], Rank: s_rankPrograms[direction],
                Gather: s_gatherPrograms[(direction * 2) + (Ascending ? 1 : 0)]);
            context.ShaderGraph((Programs: programs,
                Thresholds: new Vector2(ThresholdMin / 100, ThresholdMax / 100)), static (graph, state) =>
            {
                ShaderImage prepared = graph.Image2D("prepared", graph.Source);
                ShaderImage ranks = graph.Image2D("ranks", graph.Source);
                ShaderImage output = graph.Image2D("sorted", graph.Source);
                prepared.RasterSource = ranks.RasterSource = output.RasterSource = graph.Source;
                graph.Fullscreen("prepare", state.Programs.Prepare, pass =>
                {
                    pass.SampledImage(0, graph.Source, SamplerState.NearestClamp);
                    pass.ColorAttachment(0, prepared);
                    pass.PushConstants(state.Thresholds);
                });
                graph.Fullscreen("rank", state.Programs.Rank, pass =>
                {
                    pass.SampledImage(0, prepared, SamplerState.NearestClamp);
                    pass.ColorAttachment(0, ranks);
                });
                graph.Fullscreen("gather", state.Programs.Gather, pass =>
                {
                    pass.SampledImage(0, ranks, SamplerState.NearestClamp);
                    pass.SampledImage(1, graph.Source, SamplerState.NearestClamp);
                    pass.ColorAttachment(0, output);
                });
                return graph.Output(output);
            });
        }

        private static int GetSortKeyIndex(PixelSortKey sortKey)
            => Enum.IsDefined(sortKey)
                ? (int)sortKey
                : throw new ArgumentOutOfRangeException(nameof(sortKey), sortKey, null);

        private static int GetDirectionIndex(PixelSortDirection direction)
            => Enum.IsDefined(direction)
                ? (int)direction
                : throw new ArgumentOutOfRangeException(nameof(direction), direction, null);
    }
}
