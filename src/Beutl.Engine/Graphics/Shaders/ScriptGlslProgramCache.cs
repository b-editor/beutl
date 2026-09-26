using Beutl.Graphics.Backend;

namespace Beutl.Graphics.Shaders;

// Owned by one C# effect resource. Leases keep recorded/executing programs alive across eviction.
internal sealed class ScriptGlslProgramCache : IDisposable
{
    private readonly object _contextDomain = new();
    private readonly ProgramCache<GLSLFilterPipeline> _programs = new(
        static program => program.RetainedByteSize,
        maxRetainedBytes: 8 * 1024 * 1024);

    internal ProgramCacheStatistics Statistics => _programs.Statistics;

    public GLSLShader Create(string source, int inputCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentOutOfRangeException.ThrowIfLessThan(inputCount, 1);
        if (GraphicsContextFactory.SharedContext is not { Supports3DRendering: true } graphics)
            throw new InvalidOperationException("GLSL execution requires a Vulkan graphics context.");

        _programs.SynchronizeContext(_contextDomain, graphics);
        var context = new ProgramCacheContextKey(
            _contextDomain, graphics, "GLSL-script", "linear-premultiplied-rgba16f", inputCount);
        ShaderProgramIdentity identity = ShaderProgramIdentity.CreateSpirv(source);
        ProgramCacheLease<GLSLFilterPipeline> lease = _programs.GetOrCreate(
            identity, context, (graphics, source, inputCount),
            static state => GLSLFilterPipeline.Create(
                state.graphics, state.source, ShaderOutputCoverage.MayLeavePixelsUnwritten,
                inputCount: state.inputCount)
                ?? throw new InvalidOperationException("Failed to compile GLSL shader."));
        return new GLSLShader(lease);
    }

    public void Dispose() => _programs.Dispose();
}
