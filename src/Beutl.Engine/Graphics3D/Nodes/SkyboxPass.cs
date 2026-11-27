using System.Numerics;
using Beutl.Graphics.Backend;
using Beutl.Graphics3D.Camera;
using Beutl.Graphics3D.Skyboxes;

namespace Beutl.Graphics3D.Nodes;

/// <summary>Draws an environment into a depthless, single-sample background for the lighting pass.</summary>
internal sealed class SkyboxPass(IGraphicsContext context, IShaderCompiler shaderCompiler)
    : GraphicsNode3D(context, shaderCompiler)
{
    private ITexture2D? _output;

    protected override void OnInitialize(int width, int height) => CreateResources(width, height);

    protected override void OnResize(int width, int height) => CreateResources(width, height);

    private void CreateResources(int width, int height)
    {
        DeviceExtentLimits.ThrowIfCannotAttach(Context, width, height);

        ITexture2D? output = null;
        IRenderPass3D? newRenderPass = null;
        IFramebuffer3D framebuffer;
        try
        {
            output = Context.CreateTexture2D(width, height, TextureFormat.RGBA16Float);
            IRenderPass3D pass = RenderPass ?? (newRenderPass = Context.CreateRenderPass3D(
                [TextureFormat.RGBA16Float], depthFormat: null));
            framebuffer = Context.CreateFramebuffer3D(pass, [output], depthTexture: null);
        }
        catch
        {
            DisposeResources([newRenderPass, output]);
            throw;
        }

        IFramebuffer3D? oldFramebuffer = Framebuffer;
        ITexture2D? oldOutput = _output;
        RenderPass ??= newRenderPass;
        Framebuffer = framebuffer;
        _output = output;
        DisposeResources([oldFramebuffer, oldOutput]);
    }

    public ITexture2D? Execute(Camera3D.Resource camera, Skybox3D.Resource skybox, float surfaceDensity)
    {
        float aspectRatio = (float)Width / Height;
        Matrix4x4 projection = camera is OrthographicCamera.Resource
            ? Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3, aspectRatio, 1f, 2f)
            : camera.GetProjectionMatrix(aspectRatio);
        if (!Matrix4x4.Invert(projection, out var inverseProjection)
            || !Matrix4x4.Invert(camera.GetViewMatrix(), out var inverseView))
            return null;

        inverseView.Translation = Vector3.Zero;
        Matrix4x4 worldToEnvironment = Matrix4x4.CreateScale(1, -1, -1);
        const float radiansPerDegree = MathF.PI / 180f;
        Vector3 rotation = skybox.Rotation * radiansPerDegree;
        Matrix4x4 inverseRotation = Matrix4x4.Transpose(
            Matrix4x4.CreateFromYawPitchRoll(rotation.Y, rotation.X, rotation.Z));

        // System.Numerics composes row-vector transforms. GLSL reads the uploaded matrix as its transpose.
        // The input NDC uses the geometry pass's viewport, with the same final Y flip as the geometry.
        Matrix4x4 clipToSky = inverseProjection * inverseView * worldToEnvironment * inverseRotation;
        if (skybox.PrepareDraw(Context, ShaderCompiler, RenderPass!, clipToSky, surfaceDensity) is not { } draw)
            return null;

        using (UsePass([]))
        {
            RenderPass!.BindPipeline(draw.Pipeline);
            RenderPass.BindDescriptorSet(draw.Pipeline, draw.Descriptors);
            RenderPass.Draw(3);
        }

        PrepareForSampling();
        return _output;
    }

    protected override void OnDispose() => DisposeResources([Framebuffer, RenderPass, _output]);
}
