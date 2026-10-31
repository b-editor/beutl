using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shaders;
using Beutl.Media;
using SkiaSharp;
using Vector2 = System.Numerics.Vector2;
using Vector4 = System.Numerics.Vector4;

namespace Beutl.Graphics.Transitions;

internal enum TransitionStyle
{
    ZoomBlur,
    WhipPan,
    Glitch,
    LightLeak,
    CrossZoom,
}

// Two whole-frame shader passes use the renderer's program cache. No native shader or image outlives a
// render request; immutable state includes everything needed to replay a frame after a seek.
internal static class StylizedTransitionRenderer
{
    public static void Draw(
        ClipTransitionContext context, TransitionStyle style, float strength,
        ClipTransitionDirection direction = ClipTransitionDirection.LeftToRight, bool zoomOut = false,
        GradientSpreadMethod tileMode = GradientSpreadMethod.Decal)
    {
        if (context.DrawSettledSide()) return;

        strength = float.IsFinite(strength) ? Math.Clamp(strength / 100, 0, 1) : 0;
        if (strength == 0)
        {
            context.DrawCrossDissolve();
            return;
        }

        float progress = context.Progress;
        StyleInfo info = GetStyleInfo(style);
        float blend = SmoothStep(info.BlendStart, info.BlendEnd, progress);
        Vector travel = ClipTransitionContext.GetDirection(direction);
        var state = new ShaderState(style, context.Graphics.Size, progress, strength, false, new Vector2(travel.X, travel.Y), zoomOut, tileMode);
        using (context.PushIsolation())
        {
            DrawSide(context, context.From, state, info.SelfWeighted ? 1 : 1 - blend, BlendMode.SrcOver);
            DrawSide(context, context.To, state with { Incoming = true }, info.SelfWeighted ? 1 : blend, BlendMode.Plus);
        }
    }

    public static void DrawZoomSide(
        ClipTransitionContext context, IReadOnlyList<Drawable.Resource> drawables,
        float scale, float weight, BlendMode blendMode, GradientSpreadMethod tileMode)
    {
        var state = new ShaderState(TransitionStyle.CrossZoom, context.Graphics.Size, context.Progress,
            0, false, Vector2.Zero, false, tileMode, scale);
        DrawSide(context, drawables, state, weight, blendMode);
    }

    private static float SmoothStep(float start, float end, float value)
    {
        float t = Math.Clamp((value - start) / (end - start), 0, 1);
        return t * t * (3 - 2 * t);
    }

    private static void DrawSide(
        ClipTransitionContext context, IReadOnlyList<Drawable.Resource> drawables,
        ShaderState state, float weight, BlendMode blendMode)
    {
        if (drawables.Count == 0 || weight <= 0) return;

        using (context.Graphics.PushBlendMode(blendMode))
        using (context.Graphics.PushOpacity(weight))
        using (context.Graphics.PushNode(
                   state, static value => new TransitionShaderNode(value),
                   static (node, value) => node.Update(value)))
        using (context.Graphics.PushClip(new Rect(state.Size)))
        {
            foreach (Drawable.Resource drawable in drawables)
                context.Graphics.DrawDrawable(drawable);
        }
    }

    private readonly record struct ShaderState(
        TransitionStyle Style, Size Size, float Progress, float Strength, bool Incoming, Vector2 Direction, bool ZoomOut,
        GradientSpreadMethod TileMode, float Scale = 1);

    // A style's shader, the progress window over which the incoming side takes over, whether the shader weights
    // the two sides itself, and the uniforms it declares beyond the common ones.
    private readonly record struct StyleInfo(
        SkslSource Source, float BlendStart, float BlendEnd, bool SelfWeighted, bool UsesDirection, bool UsesTileMode,
        bool UsesScale);

    private static StyleInfo GetStyleInfo(TransitionStyle style) => style switch
    {
        TransitionStyle.ZoomBlur => new(s_zoomBlurSource, 0.35f, 0.65f, SelfWeighted: false,
            UsesDirection: false, UsesTileMode: true, UsesScale: false),
        TransitionStyle.WhipPan => new(s_whipPanSource, 0.35f, 0.65f, SelfWeighted: false,
            UsesDirection: true, UsesTileMode: false, UsesScale: false),
        // The cut varies by scan band, so the shader applies complementary weights itself.
        TransitionStyle.Glitch => new(s_glitchSource, 0.35f, 0.65f, SelfWeighted: true,
            UsesDirection: false, UsesTileMode: false, UsesScale: false),
        TransitionStyle.LightLeak => new(s_lightLeakSource, 0.45f, 0.55f, SelfWeighted: false,
            UsesDirection: false, UsesTileMode: false, UsesScale: false),
        TransitionStyle.CrossZoom => new(s_crossZoomSource, 0.35f, 0.65f, SelfWeighted: false,
            UsesDirection: false, UsesTileMode: true, UsesScale: true),
        _ => throw new ArgumentOutOfRangeException(nameof(style), style, null),
    };

    private sealed class TransitionShaderNode(ShaderState state) : ContainerRenderNode
    {
        private ShaderState _state = state;

        public bool Update(ShaderState value)
        {
            if (_state == value) return false;
            _state = value;
            MarkChanged();
            return true;
        }

        public override void Process(RenderNodeContext context)
        {
            if (context.Inputs.Count == 0) return;

            ShaderState value = _state;
            StyleInfo info = GetStyleInfo(value.Style);
            var frame = new Rect(value.Size);
            RenderFragmentHandle input = context.Layer(context.Inputs, frame);
            ShaderDescription shader = ShaderDescription.WholeSource(
                info.Source,
                RenderBoundsContract.CreateFullInput(frame, static (bounds, _) => bounds),
                bindings =>
                {
                    bindings.Uniform("resolution", Vector2.Zero, static (writer, _, execution) =>
                        writer.Set(new Vector2(execution.SemanticOutputSize.Width, execution.SemanticOutputSize.Height)));
                    bindings.Uniform("parameters", new Vector4(value.Progress, value.Strength, value.Incoming ? 1 : 0, value.ZoomOut ? -1 : 1));
                    if (info.UsesDirection)
                        bindings.Uniform("direction", value.Direction);
                    if (info.UsesTileMode)
                        bindings.Uniform("tileMode", (int)value.TileMode.ToSKShaderTileMode());
                    if (info.UsesScale)
                        bindings.Uniform("scale", value.Scale);
                },
                SKShaderTileMode.Decal,
                hitTest: RenderHitTestContract.OutputBounds);
            context.Publish(context.Shader(input, shader));
        }
    }

    private const string CommonSource = """
        uniform shader src;
        uniform float2 resolution;
        // Progress, strength, incoming side, zoom direction (positive is in).
        uniform float4 parameters;

        half4 sampleFrame(float2 uv) {
            // Reflect frame edges during fast camera moves, without extending a small transparent
            // drawable to fill the frame. Half-texel clamping avoids a transparent seam at the edge.
            float2 reflected = 1.0 - abs(mod(uv, 2.0) - 1.0);
            return src.eval(clamp(reflected * resolution, float2(0.5), resolution - 0.5));
        }
        """;

    private const string ZoomSamplingSource = """

        uniform int tileMode;

        half4 sampleZoomFrame(float2 uv) {
            // SKShaderTileMode: Clamp = 0, Repeat = 1, Mirror = 2, Decal = 3.
            if (tileMode == 3) return src.eval(uv * resolution);
            if (tileMode == 1) uv = fract(uv);
            else if (tileMode == 2) uv = 1.0 - abs(mod(uv, 2.0) - 1.0);
            // Tile the whole frame, including transparent margins, rather than the input's tight bounds.
            // Sampling texel centres avoids a transparent seam along a padded or repeated frame edge.
            return src.eval(clamp(uv * resolution, float2(0.5), resolution - 0.5));
        }
        """;

    private static readonly SkslSource s_zoomBlurSource = new(CommonSource + ZoomSamplingSource + """

            half4 main(float2 coord) {
                float p = parameters.x;
                float strength = parameters.y;
                float drive = parameters.z > 0.5 ? 1.0 - p : p;
                float zoom = 1.0 + drive * drive * strength * 2.2;
                float pulse = 4.0 * p * (1.0 - p);
                float blur = pulse * pulse * strength * 0.45;
                float2 offset = coord / resolution - 0.5;
                half4 color = half4(0.0);
                float total = 0.0;
                for (int i = 0; i < 24; i++) {
                    float t = (float(i) + 0.5) / 24.0;
                    float weight = 1.0 - abs(t * 2.0 - 1.0) * 0.7;
                    float scale = zoom / (1.0 - blur * t);
                    if (parameters.w < 0.0) scale = 1.0 / scale;
                    color += sampleZoomFrame(0.5 + offset / scale) * half(weight);
                    total += weight;
                }
                return color / half(total);
            }
            """, ShaderDescriptionKind.WholeSource);

    private static readonly SkslSource s_whipPanSource = new(CommonSource + """

            uniform float2 direction;
            half4 main(float2 coord) {
                float p = parameters.x;
                float strength = parameters.y;
                float move = p * p * (3.0 - 2.0 * p);
                float pulse = 4.0 * p * (1.0 - p);
                float2 uv = coord / resolution - direction * (move - parameters.z) * strength;
                half4 color = half4(0.0);
                for (int i = 0; i < 24; i++) {
                    float t = (float(i) + 0.5) / 24.0 - 0.5;
                    color += sampleFrame(uv + direction * t * pulse * pulse * strength * 0.3);
                }
                return color / 24.0;
            }
            """, ShaderDescriptionKind.WholeSource);

    private static readonly SkslSource s_glitchSource = new(CommonSource + """

            float noise(float2 value) {
                return fract(sin(dot(value, float2(127.1, 311.7))) * 43758.5453);
            }
            half4 main(float2 coord) {
                float p = parameters.x;
                float strength = parameters.y;
                float pulse = 4.0 * p * (1.0 - p);
                float2 uv = coord / resolution;
                float band = floor(uv.y * 24.0);
                float tick = floor(p * 24.0);
                float active = step(0.55, noise(float2(band, tick)));
                float shift = (noise(float2(band + 31.0, tick)) - 0.5) * active * 0.32;
                uv.x += (shift + sin(tick * 2.3) * 0.012) * strength * pulse;
                float split = 0.018 * strength * pulse * (1.0 + active);
                half4 red = sampleFrame(uv + float2(split, 0.0));
                half4 green = sampleFrame(uv);
                half4 blue = sampleFrame(uv - float2(split, 0.0));
                float3 straight = float3(
                    red.r / max(float(red.a), 0.0001),
                    green.g / max(float(green.a), 0.0001),
                    blue.b / max(float(blue.a), 0.0001));
                float scan = 1.0 - step(0.8, fract(uv.y * 90.0)) * 0.12 * strength * pulse;
                float stagger = (noise(float2(band, 7.0)) - 0.5) * 0.28 * strength * pulse;
                float cut = smoothstep(0.485, 0.515, p + stagger);
                float weight = parameters.z > 0.5 ? cut : 1.0 - cut;
                return half4(half3(straight * float(green.a) * scan), green.a) * half(weight);
            }
            """, ShaderDescriptionKind.WholeSource);

    private static readonly SkslSource s_lightLeakSource = new(CommonSource + """

            half4 main(float2 coord) {
                half4 color = src.eval(coord);
                float p = parameters.x;
                float2 uv = coord / resolution;
                float pulse = 4.0 * p * (1.0 - p);
                float axis = uv.x + uv.y * 0.18 - (-0.3 + p * 1.75);
                float haze = exp(-axis * axis / 0.12);
                float core = exp(-axis * axis / 0.004);
                float streak = exp(-pow((uv.y - 0.46) / 0.035, 2.0)) * haze;
                float flash = exp(-pow((p - 0.5) / 0.16, 2.0));
                float3 glow = float3(1.0, 0.22, 0.035) * haze * 0.75
                    + float3(1.0, 0.8, 0.45) * core * 1.4
                    + float3(1.0, 0.65, 0.3) * streak * 0.4
                    + float3(0.3, 0.22, 0.15) * flash;
                glow = clamp(glow * parameters.y * pulse * pulse, 0.0, 1.0);
                // Light changes the existing pixels' color, never the transparency of an empty clip.
                return half4(mix(color.rgb, half3(color.a), half3(glow)), color.a);
            }
            """, ShaderDescriptionKind.WholeSource);

    private static readonly SkslSource s_crossZoomSource = new(CommonSource + ZoomSamplingSource + """

            uniform float scale;
            half4 main(float2 coord) {
                return sampleZoomFrame(0.5 + (coord / resolution - 0.5) / scale);
            }
            """, ShaderDescriptionKind.WholeSource);
}
