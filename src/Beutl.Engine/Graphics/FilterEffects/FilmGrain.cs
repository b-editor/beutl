using System.ComponentModel.DataAnnotations;
using System.Numerics;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shaders;
using Beutl.Language;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.FilmGrain), ResourceType = typeof(GraphicsStrings))]
public sealed partial class FilmGrain : FilterEffect
{
    private const int CellBits = 14;
    private const int CellPeriod = 1 << CellBits;
    private const ulong CellMask = CellPeriod - 1;

    private static readonly SkslSource s_shaderSource = SkslSource.WholeSource(
        SkslSnippets.SrgbTransfer + "\n" + $$"""
        uniform shader src;
        uniform float2 origin;
        uniform float cellSize;
        uniform float amount;
        uniform float softness;
        uniform float chroma;
        uniform float3 noiseKey;

        const float PERIOD = {{CellPeriod}}.0;
        const float3 LUMA = float3(0.2126, 0.7152, 0.0722);

        // Keep the integer-valued operations below 2^24. Powers of two make the wraps exact in float,
        // so the CPU and GPU get the same random values without depending on transcendental approximations.
        float scramble(float value) {
            float x = mod(value * 625.0, PERIOD);
            return floor(x * mod(x, 1024.0) / 32.0) + x * 73.0;
        }

        float gaussian(float x, float y, float key) {
            y = mod(y + scramble(mod(x + key, PERIOD)), PERIOD);
            x = mod(x + scramble(y), PERIOD);
            float2 state = float2(x, y);
            // Each coordinate contributes two 7-bit uniforms. Average their bin centers and remove the mean.
            float2 digits = mod(state, 128.0) + floor(state / 128.0);
            return (digits.x + digits.y + 2.0) / 512.0 - 0.5;
        }

        float3 grainAt(float2 cell) {
            float2 p = mod(mod(cell, PERIOD) + noiseKey.xy, PERIOD);
            float x = mod(p.x + scramble(p.y), PERIOD);
            float mono = gaussian(x, p.y, noiseKey.z);
            if (chroma == 0.0) return float3(mono);

            float3 color = float3(gaussian(x, p.y, noiseKey.z + 3191.0),
                                  gaussian(x, p.y, noiseKey.z + 7481.0),
                                  gaussian(x, p.y, noiseKey.z + 11551.0));
            return mix(float3(mono), color, chroma);
        }

        float3 grain(float2 fragCoord) {
            if (cellSize < 1.0) return grainAt(floor(fragCoord));

            // Put the interpolation samples at the hard cells' centers, so they agree at one pixel per cell.
            float2 position = (fragCoord - origin) / cellSize - 0.5;
            float2 cell = floor(position);
            float2 f = fract(position);
            float2 nearest = step(float2(0.5), f);
            if (softness == 0.0) return grainAt(cell + nearest);

            float2 t = mix(nearest, smoothstep(float2(0.0), float2(1.0), f), softness);
            return mix(mix(grainAt(cell), grainAt(cell + float2(1.0, 0.0)), t.x),
                       mix(grainAt(cell + float2(0.0, 1.0)), grainAt(cell + 1.0), t.x), t.y);
        }

        half4 main(float2 fragCoord) {
            half4 color = src.eval(fragCoord);
            float alpha = color.a;
            if (alpha == 0.0) return color;

            float3 rgb = linearToSrgb(float3(color.rgb) / alpha);
            float luma = clamp(dot(rgb, LUMA), 0.0, 1.0);
            float weight = 4.0 * luma * (1.0 - luma);
            if (weight == 0.0) return color;

            // Averaging subpixel cells reduces the noise's standard deviation in proportion to the device cell size.
            float amplitude = amount * weight * min(cellSize, 1.0);
            float3 noisy = rgb + amplitude * grain(fragCoord);
            // Limit new excursions outside the display range while preserving existing extended-range values.
            noisy = clamp(noisy, min(rgb, float3(0.0)), max(rgb, float3(1.0)));
            return half4(half3(srgbToLinear(noisy) * alpha), color.a);
        }
        """);

    public FilmGrain() => ScanProperties<FilmGrain>();

    [Display(Name = nameof(GraphicsStrings.Amount), ResourceType = typeof(GraphicsStrings))]
    [Range(0, float.MaxValue)]
    public IExpressionProperty<float> Amount { get; } = Property.CreateAnimatable(25f);

    [Display(Name = nameof(GraphicsStrings.FilmGrain_Size), Description = nameof(GraphicsStrings.FilmGrain_Size_Description),
        ResourceType = typeof(GraphicsStrings))]
    [Range(0.1f, float.MaxValue)]
    public IExpressionProperty<float> Size { get; } = Property.CreateAnimatable(1.5f);

    [Display(Name = nameof(GraphicsStrings.FilmGrain_Softness), Description = nameof(GraphicsStrings.FilmGrain_Softness_Description),
        ResourceType = typeof(GraphicsStrings))]
    [Range(0, 100)]
    public IExpressionProperty<float> Softness { get; } = Property.CreateAnimatable(50f);

    [Display(Name = nameof(GraphicsStrings.FilmGrain_Color), Description = nameof(GraphicsStrings.FilmGrain_Color_Description),
        ResourceType = typeof(GraphicsStrings))]
    [Range(0, 100)]
    public IExpressionProperty<float> Color { get; } = Property.CreateAnimatable(0f);

    [Display(Name = nameof(GraphicsStrings.FilmGrain_Seed), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<int> Seed { get; } = Property.CreateAnimatable(0);

    [Display(Name = nameof(GraphicsStrings.FilmGrain_Animate), Description = nameof(GraphicsStrings.FilmGrain_Animate_Description),
        ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<bool> Animate { get; } = Property.CreateAnimatable(true);

    private static Vector3 CreateNoiseKey(int seed, long frame)
    {
        // The SplitMix64 finalizer mixes the full seed and frame before conversion to small, exact float offsets.
        ulong key = unchecked((ulong)frame + ((ulong)(uint)seed + 1) * 0x9E3779B97F4A7C15UL);
        key = unchecked((key ^ (key >> 30)) * 0xBF58476D1CE4E5B9UL);
        key = unchecked((key ^ (key >> 27)) * 0x94D049BB133111EBUL);
        key ^= key >> 31;
        return new Vector3(key & CellMask, (key >> CellBits) & CellMask, (key >> (2 * CellBits)) & CellMask);
    }

    private static void BindOrigin(ShaderUniformWriter writer, Vector2 _, ShaderExecutionContext execution)
    {
        RenderPlacement placement = execution.OutputPlacement;
        var origin = execution.OutputBounds.Position - placement.LogicalOrigin;
        writer.Set(new Vector2(origin.X, origin.Y) * placement.Scale.Value);
    }

    public new partial class Resource
    {
        private long _frameIndex;

        partial void PostReconcile(FilmGrain obj, CompositionContext context)
        {
            if (!Animate) return;

            // MediaTime keeps frame boundaries exact and measures them from the clip's own start.
            MediaTime localTime = context.Time - obj.TimeRange.Value.Start;
            long frameIndex = localTime.ToFrame(context.FrameRate, MidpointRounding.ToNegativeInfinity);
            if (frameIndex != _frameIndex)
            {
                _frameIndex = frameIndex;
                Version++;
            }
        }

        protected override void ApplyTo(FilterEffectContext context)
        {
            float amount = Amount / 100f;
            // A pass through an intermediate target can resample edges even if it adds no grain.
            if (amount <= 0 || !float.IsFinite(amount) || !float.IsFinite(Size)
                || !float.IsFinite(Softness) || !float.IsFinite(Color))
                return;

            context.Shader(ShaderDescription.WholeSource(
                s_shaderSource,
                RenderBoundsContract.Identity,
                bindings =>
                {
                    bindings.Uniform("origin", Vector2.Zero, BindOrigin);
                    bindings.Uniform("cellSize", MathF.Max(0.1f, Size),
                        static (writer, size, execution) => writer.Set(
                            MathF.Min(size * execution.OutputPlacement.Scale.Value, float.MaxValue)));
                    bindings.Uniform("amount", amount);
                    bindings.Uniform("softness", Math.Clamp(Softness / 100f, 0f, 1f));
                    bindings.Uniform("chroma", Math.Clamp(Color / 100f, 0f, 1f));
                    bindings.Uniform("noiseKey", CreateNoiseKey(Seed, Animate ? _frameIndex : 0));
                },
                SKShaderTileMode.Decal));
        }
    }
}
