using System.ComponentModel.DataAnnotations;
using System.Numerics;
using System.Runtime.InteropServices;
using Beutl.Engine;
using Beutl.Graphics.Backend;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics3D.Skyboxes;

/// <summary>A skybox with a zenith-to-ground gradient and a sun.</summary>
[Display(Name = nameof(GraphicsStrings.ProceduralSkybox), ResourceType = typeof(GraphicsStrings))]
public sealed partial class ProceduralSkybox : Skybox3D
{
    public ProceduralSkybox()
    {
        ScanProperties<ProceduralSkybox>();
    }

    [Display(Name = nameof(GraphicsStrings.ProceduralSkybox_SkyColor), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<Color> SkyColor { get; } = Property.CreateAnimatable(Color.FromRgb(82, 145, 230));

    [Display(Name = nameof(GraphicsStrings.ProceduralSkybox_HorizonColor), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<Color> HorizonColor { get; } = Property.CreateAnimatable(Color.FromRgb(191, 218, 244));

    [Display(Name = nameof(GraphicsStrings.ProceduralSkybox_GroundColor), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<Color> GroundColor { get; } = Property.CreateAnimatable(Color.FromRgb(88, 82, 76));

    [Display(Name = nameof(GraphicsStrings.ProceduralSkybox_HorizonExponent), ResourceType = typeof(GraphicsStrings))]
    [Range(0.01f, 64f), NumberStep(0.1, 0.01)]
    public IExpressionProperty<float> HorizonExponent { get; } = Property.CreateAnimatable(0.65f);

    /// <summary>Gets the direction toward the sun in Y-up environment space.</summary>
    [Display(Name = nameof(GraphicsStrings.ProceduralSkybox_SunDirection), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<Vector3> SunDirection { get; } = Property.CreateAnimatable(new Vector3(0.35f, 0.4f, -1f));

    [Display(Name = nameof(GraphicsStrings.ProceduralSkybox_SunColor), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<Color> SunColor { get; } = Property.CreateAnimatable(Color.FromRgb(255, 241, 204));

    /// <summary>Gets the angular radius of the sun.</summary>
    [Display(Name = nameof(GraphicsStrings.ProceduralSkybox_SunSize), ResourceType = typeof(GraphicsStrings))]
    [Range(0f, 90f), NumberStep(1, 0.1), Unit(PropertyUnit.Degrees)]
    public IExpressionProperty<float> SunSize { get; } = Property.CreateAnimatable(3f);

    [Display(Name = nameof(GraphicsStrings.ProceduralSkybox_SunIntensity), ResourceType = typeof(GraphicsStrings))]
    [Range(0f, float.MaxValue), NumberStep(0.1, 0.01)]
    public IExpressionProperty<float> SunIntensity { get; } = Property.CreateAnimatable(2f);

    public partial class Resource
    {
        protected internal override (IPipeline3D Pipeline, IDescriptorSet Descriptors)? PrepareDraw(
            IGraphicsContext context, IShaderCompiler shaderCompiler, IRenderPass3D renderPass,
            Matrix4x4 clipToSky, float surfaceDensity)
        {
            var pipeline = GetPipeline<ProceduralUniforms>(context, shaderCompiler, renderPass, FragmentShader);
            float length = SunDirection.Length();
            float sunSize = Math.Clamp(SunSize, 0f, 90f) * (MathF.PI / 180f);
            var uniforms = new ProceduralUniforms
            {
                Sky = GetUniforms(clipToSky),
                SkyColor = SkyColor.ToLinearPremultiplied(),
                HorizonColor = HorizonColor.ToLinearPremultiplied(),
                GroundColor = GroundColor.ToLinearPremultiplied(),
                SunRadianceAndExponent = new Vector4(
                    SunColor.ToLinearPremultiplied().AsVector3() * MathF.Max(0f, SunIntensity),
                    Math.Clamp(HorizonExponent, 0.01f, 64f)),
                SunDirectionAndSize = length > 0f && float.IsFinite(length)
                    ? new Vector4(SunDirection / length, sunSize)
                    : Vector4.Zero,
            };
            return pipeline.PrepareDraw(pipeline.AcquireBindings(), uniforms);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ProceduralUniforms
        {
            public SkyboxUniforms Sky;
            public Vector4 SkyColor;
            public Vector4 HorizonColor;
            public Vector4 GroundColor;
            public Vector4 SunRadianceAndExponent;
            public Vector4 SunDirectionAndSize;
        }

        private const string FragmentShader = FragmentHeader + """
            layout(std140, binding = 0) uniform ProceduralSkyboxUBO {
                SkyboxUniforms sky;
                vec4 skyColor;
                vec4 horizonColor;
                vec4 groundColor;
                vec4 sunRadianceAndExponent;
                vec4 sunDirectionAndSize;
            };

            void main() {
                vec3 dir = skyDirection(sky);
                float gradient = pow(clamp(abs(dir.y), 0.0, 1.0), sunRadianceAndExponent.w);
                vec4 color = mix(horizonColor, dir.y >= 0.0 ? skyColor : groundColor, gradient);
                float radius = sunDirectionAndSize.w;
                if (radius > 0.0) {
                    float angle = acos(clamp(dot(dir, sunDirectionAndSize.xyz), -1.0, 1.0));
                    float disk = 1.0 - smoothstep(0.8 * radius, radius, angle);
                    float glow = 0.15 * (1.0 - smoothstep(0.0, 3.0 * radius, angle));
                    color.rgb += color.a * sunRadianceAndExponent.rgb * (disk + glow);
                }
                outColor = finishSky(color, sky);
            }
            """;
    }
}
