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
}

// Two whole-frame shader passes use the renderer's program cache. No native shader or image outlives a
// render request; immutable state includes everything needed to replay a frame after a seek.
internal static class StylizedTransitionRenderer
{
    public static void Draw(
        ClipTransitionContext context, TransitionStyle style, float strength,
        ClipTransitionDirection direction = ClipTransitionDirection.LeftToRight)
    {
        float progress = context.Progress;
        if (progress <= 0 || progress >= 1)
        {
            context.DrawClip(progress <= 0 ? context.From : context.To);
            return;
        }

        strength = float.IsFinite(strength) ? Math.Clamp(strength / 100, 0, 1) : 0;
        if (strength == 0)
        {
            context.DrawCrossDissolve();
            return;
        }

        float blend = style == TransitionStyle.LightLeak
            ? SmoothStep(0.45f, 0.55f, progress)
            : SmoothStep(0.35f, 0.65f, progress);
        Vector travel = ClipTransitionContext.GetDirection(direction);
        var state = new ShaderState(style, context.Graphics.Size, progress, strength, false, new Vector2(travel.X, travel.Y));
        using (context.PushIsolation())
        {
            // Glitch's cut varies by scan band. Its shaders apply complementary weights themselves.
            DrawSide(context, context.From, state, style == TransitionStyle.Glitch ? 1 : 1 - blend, BlendMode.SrcOver);
            DrawSide(context, context.To, state with { Incoming = true }, style == TransitionStyle.Glitch ? 1 : blend, BlendMode.Plus);
        }
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
        TransitionStyle Style, Size Size, float Progress, float Strength, bool Incoming, Vector2 Direction);

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
            var frame = new Rect(value.Size);
            RenderFragmentHandle input = context.Layer(context.Inputs, frame);
            ShaderDescription shader = ShaderDescription.WholeSource(
                s_sources[(int)value.Style],
                RenderBoundsContract.CreateFullInput(frame, static (bounds, _) => bounds),
                bindings =>
                {
                    bindings.Uniform("resolution", Vector2.Zero, static (writer, _, execution) =>
                        writer.Set(new Vector2(execution.SemanticOutputSize.Width, execution.SemanticOutputSize.Height)));
                    bindings.Uniform("parameters", new Vector4(value.Progress, value.Strength, value.Incoming ? 1 : 0, 0));
                    if (value.Style == TransitionStyle.WhipPan)
                        bindings.Uniform("direction", value.Direction);
                },
                SKShaderTileMode.Decal,
                hitTest: RenderHitTestContract.OutputBounds);
            context.Publish(context.Shader(input, shader));
        }
    }

    private const string CommonSource = """
        uniform shader src;
        uniform float2 resolution;
        // Progress, strength, incoming side, reserved.
        uniform float4 parameters;

        half4 sampleFrame(float2 uv) {
            // Reflect frame edges during fast camera moves, without extending a small transparent
            // drawable to fill the frame. Half-texel clamping avoids a transparent seam at the edge.
            float2 reflected = 1.0 - abs(mod(uv, 2.0) - 1.0);
            return src.eval(clamp(reflected * resolution, float2(0.5), resolution - 0.5));
        }
        """;

    private static readonly SkslSource[] s_sources =
    [
        new(CommonSource + """

            half4 main(float2 coord) {
                float p = parameters.x;
                float strength = parameters.y;
                float drive = parameters.z > 0.5 ? 1.0 - p : p;
                float zoom = 1.0 + drive * drive * strength * 2.2;
                float pulse = 4.0 * p * (1.0 - p);
                float blur = pulse * pulse * strength * 0.45;
                float2 offset = (coord / resolution - 0.5) / zoom;
                half4 color = half4(0.0);
                float total = 0.0;
                for (int i = 0; i < 24; i++) {
                    float t = (float(i) + 0.5) / 24.0;
                    float weight = 1.0 - abs(t * 2.0 - 1.0) * 0.7;
                    color += sampleFrame(0.5 + offset * (1.0 - blur * t)) * half(weight);
                    total += weight;
                }
                return color / half(total);
            }
            """, ShaderDescriptionKind.WholeSource),
        new(CommonSource + """

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
            """, ShaderDescriptionKind.WholeSource),
        new(CommonSource + """

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
            """, ShaderDescriptionKind.WholeSource),
        new(CommonSource + """

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
            """, ShaderDescriptionKind.WholeSource),
    ];
}
