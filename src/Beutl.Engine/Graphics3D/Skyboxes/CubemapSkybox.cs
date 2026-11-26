using System.ComponentModel.DataAnnotations;
using System.Numerics;
using Beutl.Engine;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Graphics3D.Materials;
using Beutl.Graphics3D.Nodes;
using Beutl.Graphics3D.Textures;
using Beutl.Language;

namespace Beutl.Graphics3D.Skyboxes;

/// <summary>A skybox sampled from six faces in Y-up environment space.</summary>
[Display(Name = nameof(GraphicsStrings.CubemapSkybox), ResourceType = typeof(GraphicsStrings))]
public sealed partial class CubemapSkybox : Skybox3D
{
    public CubemapSkybox()
    {
        ScanProperties<CubemapSkybox>();
    }

    [Display(Name = nameof(GraphicsStrings.CubemapSkybox_PositiveX), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<TextureSource?> PositiveX { get; } = Property.CreateExpression<TextureSource?>();

    [Display(Name = nameof(GraphicsStrings.CubemapSkybox_NegativeX), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<TextureSource?> NegativeX { get; } = Property.CreateExpression<TextureSource?>();

    [Display(Name = nameof(GraphicsStrings.CubemapSkybox_PositiveY), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<TextureSource?> PositiveY { get; } = Property.CreateExpression<TextureSource?>();

    [Display(Name = nameof(GraphicsStrings.CubemapSkybox_NegativeY), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<TextureSource?> NegativeY { get; } = Property.CreateExpression<TextureSource?>();

    [Display(Name = nameof(GraphicsStrings.CubemapSkybox_PositiveZ), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<TextureSource?> PositiveZ { get; } = Property.CreateExpression<TextureSource?>();

    [Display(Name = nameof(GraphicsStrings.CubemapSkybox_NegativeZ), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<TextureSource?> NegativeZ { get; } = Property.CreateExpression<TextureSource?>();

    public partial class Resource
    {
        private const int FaceCount = 6;

        private IGraphicsContext? _cubeContext;
        private ITextureCube? _cube;
        private ITexture2D? _transparentFace;
        private FaceInput[] _uploadedFaces = new FaceInput[FaceCount];

        protected internal override bool HasContent
            => EnumerateFaces().Any(static face => face is { IsEnabled: true } source && source.HasContent);

        protected internal override IEnumerable<TextureSource.Resource> EnumerateTextureSources()
            => EnumerateFaces().OfType<TextureSource.Resource>();

        private IEnumerable<TextureSource.Resource?> EnumerateFaces()
        {
            yield return PositiveX;
            yield return NegativeX;
            yield return PositiveY;
            yield return NegativeY;
            yield return PositiveZ;
            yield return NegativeZ;
        }

        protected internal override (IPipeline3D Pipeline, IDescriptorSet Descriptors)? PrepareDraw(
            IGraphicsContext context, IShaderCompiler shaderCompiler, IRenderPass3D renderPass,
            Matrix4x4 clipToSky, float surfaceDensity)
        {
            if (GetCube(context, surfaceDensity) is not { } cube)
                return null;

            var pipeline = GetPipeline<SkyboxUniforms>(
                context, shaderCompiler, renderPass, FragmentShader, SamplerAddressMode.ClampToEdge);
            var bindings = pipeline.AcquireBindings();
            bindings.Descriptors.UpdateTextureCube(1, cube, pipeline.Sampler);
            return pipeline.PrepareDraw(bindings, GetUniforms(clipToSky));
        }

        private ITextureCube? GetCube(IGraphicsContext context, float surfaceDensity)
        {
            if (!ReferenceEquals(_cubeContext, context))
            {
                ReleaseCube();
                _transparentFace?.Dispose();
                _transparentFace = null;
                _cubeContext = context;
            }

            var faces = new FaceInput[FaceCount];
            int index = 0;
            int size = 0;
            foreach (TextureSource.Resource? source in EnumerateFaces())
            {
                if (source is { IsEnabled: true } && source.GetTexture(context, surfaceDensity) is { } texture)
                {
                    NestedRenderTargetBinding? binding = null;
                    if (source is IRecordedTextureSource recorded)
                        NestedRenderTargetBindingScope.TryGet(recorded, out binding);

                    faces[index] = new FaceInput(source, source.Version, texture, binding);
                    // Use the largest short edge so a very wide or tall image doesn't force a huge square.
                    size = Math.Max(size, Math.Min(texture.Width, texture.Height));
                }
                index++;
            }

            if (size == 0)
            {
                ReleaseCube();
                return null;
            }

            if (context.MaxCubeFaceDimension > 0)
                size = Math.Min(size, context.MaxCubeFaceDimension);

            if (_cube is { } existing && existing.Size == size)
            {
                for (int i = 0; i < faces.Length; i++)
                {
                    if (faces[i] == _uploadedFaces[i])
                        continue;

                    UploadFace(context, existing, i, faces[i].Texture);
                    _uploadedFaces[i] = faces[i];
                }

                return existing;
            }

            ITextureCube replacement = context.CreateTextureCube(size, TextureFormat.RGBA16Float);
            try
            {
                for (int i = 0; i < faces.Length; i++)
                    UploadFace(context, replacement, i, faces[i].Texture);
            }
            catch
            {
                replacement.Dispose();
                throw;
            }

            ITextureCube? previous = _cube;
            _cube = replacement;
            _uploadedFaces = faces;
            previous?.Dispose();
            return replacement;
        }

        private void UploadFace(IGraphicsContext context, ITextureCube cube, int index, ITexture2D? texture)
        {
            texture ??= _transparentFace ??= MaterialGpuResources.Create1x1Texture(context, [0, 0, 0, 0]);
            context.BlitTextureToCubeFace(texture, cube, index);
        }

        private void ReleaseCube()
        {
            _cube?.Dispose();
            _cube = null;
            Array.Clear(_uploadedFaces);
        }

        partial void PostDispose(bool disposing)
        {
            RenderNode3D.DisposeResources([_cube, _transparentFace]);
            _cube = null;
            _transparentFace = null;
            _cubeContext = null;
            Array.Clear(_uploadedFaces);
        }

        // A recorded drawable can reuse the same pooled texture with new pixels in the next nested request.
        private readonly record struct FaceInput(
            TextureSource.Resource Source, int Version, ITexture2D? Texture, NestedRenderTargetBinding? Binding);

        private const string FragmentShader = FragmentHeader + """
            layout(std140, binding = 0) uniform SkyboxUBO { SkyboxUniforms sky; };
            layout(binding = 1) uniform samplerCube cubemap;

            void main() {
                outColor = finishSky(texture(cubemap, skyDirection(sky)), sky);
            }
            """;
    }
}
