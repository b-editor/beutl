using Beutl.Graphics.Backend;

namespace Beutl.Graphics.Shaders;

public sealed partial class ShaderPassBuilder
{
    private ShaderIndirectCommand? _indirect;
    private ShaderDepthAttachment? _depthAttachment;
    private DepthStencilState _depthStencil;
    private CullMode _cullMode;
    private FrontFace _frontFace = FrontFace.CounterClockwise;
    private int _patchControlPoints = 3;

    public void DepthAttachment(ShaderImage image, AttachmentLoadOp load = AttachmentLoadOp.Clear,
        float clearDepth = 1, uint clearStencil = 0)
    {
        EnsureGraphics();
        _graph.ValidateResource(image);
        if (!image.IsAttachment || !image.Format.IsDepthFormat() || !Enum.IsDefined(load)
            || !float.IsFinite(clearDepth) || clearDepth < 0 || clearDepth > 1)
            throw new ArgumentException("A depth attachment selects one 2D depth subresource and a clear depth between zero and one.");
        _depthAttachment = new(image, load, clearDepth, clearStencil);
    }

    public void DepthStencil(DepthStencilState state)
    {
        EnsureGraphics();
        if (!Enum.IsDefined(state.Compare) || !ValidFace(state.Front) || !ValidFace(state.Back))
            throw new ArgumentOutOfRangeException(nameof(state));
        _depthStencil = state;
        static bool ValidFace(StencilFaceState face) => Enum.IsDefined(face.Compare) && Enum.IsDefined(face.Fail)
            && Enum.IsDefined(face.DepthFail) && Enum.IsDefined(face.Pass);
    }

    public void Cull(CullMode mode, FrontFace frontFace = FrontFace.CounterClockwise)
    {
        EnsureGraphics();
        if (!Enum.IsDefined(mode) || !Enum.IsDefined(frontFace))
            throw new ArgumentOutOfRangeException(nameof(mode));
        _cullMode = mode;
        _frontFace = frontFace;
    }

    /// <summary>Sets the number of control points in each tessellation patch.</summary>
    public void PatchControlPoints(int count)
    {
        EnsureGraphics();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        _patchControlPoints = count;
        _topology = PrimitiveTopology.PatchList;
    }

    /// <summary>Reads draw commands at execution time. Values must satisfy device limits and bound vertex ranges.</summary>
    /// <remarks>Query ShaderCapabilities for multi-draw and nonzero indirect first-instance support. No CPU readback is performed.</remarks>
    public void DrawIndirect(ShaderBuffer commands, long offset = 0, int drawCount = 1, int stride = 16)
    {
        EnsureGraphics();
        SetDrawRange(false, 0, 0, 0);
        SetIndirect(commands, offset, drawCount, stride, 16);
    }

    public void DrawIndexedIndirect(ShaderBuffer commands, long offset = 0, int drawCount = 1, int stride = 20)
    {
        EnsureGraphics();
        SetDrawRange(true, 0, 0, 0);
        SetIndirect(commands, offset, drawCount, stride, 20);
    }

    /// <summary>Reads three workgroup counts at execution time. Each count must fit the device's dispatch limits.</summary>
    public void DispatchIndirect(ShaderBuffer commands, long offset = 0)
    {
        EnsureCompute();
        SetIndirect(commands, offset, 1, 12, 12);
        _threads = false;
    }

    private void SetIndirect(ShaderBuffer commands, long offset, int count, int stride, int commandSize)
    {
        _graph.ValidateResource(commands);
        var indirect = new ShaderIndirectCommand(commands, offset, count, stride, commandSize);
        indirect.Validate();
        _work = (1, 1, 1);
        _indirect = indirect;
    }

    private void ValidateDepth()
    {
        if (_depthAttachment is { } depth && _bindings.Values.Any(b => !b.Metadata && b.Resource is ShaderImage image && image.Overlaps(depth.Image)))
            throw new InvalidOperationException($"Pass '{_name}' aliases its depth attachment.");
        if ((_depthStencil.TestEnabled || _depthStencil.WriteEnabled || _depthStencil.StencilEnabled) && _depthAttachment is null)
            throw new InvalidOperationException($"Pass '{_name}' requires a depth/stencil attachment for its state.");
        if (_depthStencil.StencilEnabled && _depthAttachment?.Image.Format.HasStencil() != true)
            throw new InvalidOperationException($"Pass '{_name}' requires a stencil aspect.");
        if (_depthAttachment?.Load == AttachmentLoadOp.DontCare && (_depthStencil.ReadsDepth || _depthStencil.ReadsStencil))
            throw new InvalidOperationException($"Pass '{_name}' tests undefined depth/stencil contents.");
    }
}
