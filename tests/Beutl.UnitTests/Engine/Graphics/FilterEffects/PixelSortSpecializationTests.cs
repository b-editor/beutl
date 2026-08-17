using System.Runtime.InteropServices;
using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.FilterEffects;

[TestFixture]
[NonParallelizable]
public sealed class PixelSortSpecializationTests
{
    private const string RuntimePrepareShaderSource = """
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

    private const string RuntimeRankShaderSource = """
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

            if (myKey < 0.0005) {
                outColor = vec4(0.0);
                return;
            }

            int segStart = idx;
            for (int s = idx - 1; s >= 0; s--) {
                ivec2 c = (pc.sortDir == 0) ? ivec2(s, lineIdx) : ivec2(lineIdx, s);
                if (texelFetch(srcTexture, c, 0).a < 0.0005) break;
                segStart = s;
            }

            int segEnd = idx;
            for (int s = idx + 1; s < maxIdx; s++) {
                ivec2 c = (pc.sortDir == 0) ? ivec2(s, lineIdx) : ivec2(lineIdx, s);
                if (texelFetch(srcTexture, c, 0).a < 0.0005) break;
                segEnd = s;
            }

            int rank = 0;
            for (int j = segStart; j <= segEnd; j++) {
                if (j == idx) continue;
                ivec2 c = (pc.sortDir == 0) ? ivec2(j, lineIdx) : ivec2(lineIdx, j);
                float otherKey = texelFetch(srcTexture, c, 0).a;
                if (otherKey < myKey || (otherKey == myKey && j < idx)) {
                    rank++;
                }
            }

            outColor = vec4(
                float(rank & 255) / 255.0,
                float((rank >> 8) & 255) / 255.0,
                1.0,
                0.0
            );
        }
        """;

    private const string RuntimeGatherShaderSource = """
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

            if (rankData.b < 0.5) {
                outColor = texelFetch(originalTexture, coord, 0);
                return;
            }

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

            int targetRank = (pc.ascending == 1)
                ? (idx - segStart)
                : (segEnd - idx);

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

            outColor = originalAtIdx;
        }
        """;

    private static readonly Rect s_bounds = new(0, 0, 4, 4);
    private GLSLShader? _runtimePrepareShader;
    private GLSLShader? _runtimeRankShader;
    private GLSLShader? _runtimeGatherShader;

    private static IEnumerable<TestCaseData> SpecializationCases
    {
        get
        {
            foreach (PixelSortKey sortKey in Enum.GetValues<PixelSortKey>())
            {
                foreach (PixelSortDirection direction in Enum.GetValues<PixelSortDirection>())
                {
                    yield return new TestCaseData(sortKey, direction, true)
                        .SetName($"PixelSort_SpecializedMatchesRuntime_{sortKey}_{direction}_Ascending");
                    yield return new TestCaseData(sortKey, direction, false)
                        .SetName($"PixelSort_SpecializedMatchesRuntime_{sortKey}_{direction}_Descending");
                }
            }
        }
    }

    [OneTimeSetUp]
    public void CreateRuntimeReferenceShaders()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            _runtimePrepareShader = GLSLShader.CreateBuiltIn(RuntimePrepareShaderSource);
            _runtimeRankShader = GLSLShader.CreateBuiltIn(RuntimeRankShaderSource);
            _runtimeGatherShader = GLSLShader.CreateBuiltIn(
                RuntimeGatherShaderSource,
                hasMaskTexture: true);
        });
    }

    [OneTimeTearDown]
    public void DisposeRuntimeReferenceShaders()
    {
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            _runtimePrepareShader?.Dispose();
            _runtimeRankShader?.Dispose();
            _runtimeGatherShader?.Dispose();
        });
    }

    [TestCaseSource(nameof(SpecializationCases))]
    [Category("GpuPassFusionGpu")]
    public void SpecializedVariants_MatchPreChangeRuntimeBranches(
        PixelSortKey sortKey,
        PixelSortDirection direction,
        bool ascending)
    {
        IGraphicsContext graphicsContext = VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using RenderTarget source = RenderTarget.Create((int)s_bounds.Width, (int)s_bounds.Height)
                ?? throw new InvalidOperationException("Could not create the pixel-sort specialization source.");
            DrawDistinctPixels(source);

            using RenderTarget specialized = ApplySpecializedPixelSort(source, sortKey, direction, ascending);
            byte[] specializedPixels = specialized.Texture!.DownloadPixels();
            byte[] runtimePixels = ExecuteRuntimeReference(
                graphicsContext,
                source,
                sortKey,
                direction,
                ascending);

            Assert.That(specializedPixels, Is.EqualTo(runtimePixels));
        });
    }

    private byte[] ExecuteRuntimeReference(
        IGraphicsContext context,
        RenderTarget source,
        PixelSortKey sortKey,
        PixelSortDirection direction,
        bool ascending)
    {
        GLSLShader prepare = _runtimePrepareShader
            ?? throw new InvalidOperationException("The runtime prepare shader was not initialized.");
        GLSLShader rank = _runtimeRankShader
            ?? throw new InvalidOperationException("The runtime rank shader was not initialized.");
        GLSLShader gather = _runtimeGatherShader
            ?? throw new InvalidOperationException("The runtime gather shader was not initialized.");
        ITexture2D original = source.Texture
            ?? throw new InvalidOperationException("The pixel-sort source has no GPU texture.");
        int width = original.Width;
        int height = original.Height;
        using ITexture2D prepared = context.CreateTexture2D(width, height, TextureFormat.RGBA16Float);
        using ITexture2D ranked = context.CreateTexture2D(width, height, TextureFormat.RGBA16Float);
        using ITexture2D result = context.CreateTexture2D(width, height, TextureFormat.RGBA16Float);

        source.PrepareForSampling(RenderTargetSamplingIntent.BackendInterop);
        prepare.ExecuteSingleTarget(
            original,
            prepared,
            new RuntimePreparePushConstants
            {
                ThresholdMin = 0,
                ThresholdMax = 1,
                SortKeyType = (int)sortKey,
                SortDir = (int)direction,
                Width = width,
                Height = height,
            });
        rank.ExecuteSingleTarget(
            prepared,
            ranked,
            new RuntimeRankPushConstants
            {
                SortDir = (int)direction,
                Width = width,
                Height = height,
            });
        gather.ExecuteSingleTargetWithMask(
            ranked,
            original,
            result,
            new RuntimeGatherPushConstants
            {
                SortDir = (int)direction,
                Ascending = ascending ? 1 : 0,
                Width = width,
                Height = height,
            });

        return result.DownloadPixels();
    }

    private static RenderTarget ApplySpecializedPixelSort(
        RenderTarget source,
        PixelSortKey sortKey,
        PixelSortDirection direction,
        bool ascending)
    {
        var effect = new PixelSortEffect();
        effect.Direction.CurrentValue = direction;
        effect.SortKey.CurrentValue = sortKey;
        effect.ThresholdMin.CurrentValue = 0;
        effect.ThresholdMax.CurrentValue = 100;
        effect.Ascending.CurrentValue = ascending;

        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        using var context = new FilterEffectContext(s_bounds);
        context.ApplyTransactional(effect, resource);
        using var targets = new EffectTargets
        {
            new EffectTarget(source, s_bounds, EffectiveScale.At(1)),
        };
        using var builder = new SKImageFilterBuilder();
        using var activator = new FilterEffectActivator(
            targets,
            builder,
            RenderIntent.Delivery,
            RenderRequestPurpose.Auxiliary,
            outputScale: 1,
            workingScale: 1,
            maxWorkingScale: 1,
            deviceGridOffset: default,
            useExecutorManagedCanvas: true);

        activator.Apply(context);

        RenderTarget applied = activator.CurrentTargets.Single().RenderTarget
            ?? throw new InvalidOperationException("The specialized pixel-sort effect produced no target.");
        return applied.ShallowCopy();
    }

    private static void DrawDistinctPixels(RenderTarget target)
    {
        SKColor[] colors =
        [
            new(12, 201, 73), new(231, 42, 118), new(64, 91, 223), new(174, 219, 31),
            new(93, 17, 186), new(246, 133, 52), new(28, 168, 211), new(157, 76, 9),
            new(204, 187, 99), new(49, 234, 142), new(119, 58, 247), new(222, 105, 164),
            new(71, 149, 38), new(188, 26, 214), new(137, 196, 181), new(35, 113, 127),
        ];
        target.BeginDraw();
        SKCanvas canvas = target.Value.Canvas;
        using var paint = new SKPaint { IsAntialias = false };
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                paint.Color = colors[(y * 4) + x];
                canvas.DrawRect(SKRect.Create(x, y, 1, 1), paint);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RuntimePreparePushConstants
    {
        public float ThresholdMin;
        public float ThresholdMax;
        public int SortKeyType;
        public int SortDir;
        public float Width;
        public float Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RuntimeRankPushConstants
    {
        public int SortDir;
        public float Width;
        public float Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RuntimeGatherPushConstants
    {
        public int SortDir;
        public int Ascending;
        public float Width;
        public float Height;
    }
}
