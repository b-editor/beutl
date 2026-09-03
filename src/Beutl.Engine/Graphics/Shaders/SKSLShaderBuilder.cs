using SkiaSharp;

namespace Beutl.Graphics.Shaders;

public sealed class SKSLShaderBuilder : IDisposable
{
    private readonly SKSLShader _owner;
    private bool _disposed;

    internal SKSLShaderBuilder(SKSLShader owner, SKRuntimeEffect effect)
    {
        _owner = owner;
        Uniforms = new SKRuntimeEffectUniforms(effect);
        Children = new SKRuntimeEffectChildren(effect);
    }

    public SKRuntimeEffectUniforms Uniforms { get; }

    public SKRuntimeEffectChildren Children { get; }

    /// <summary>
    /// Builds the configured runtime shader. The caller owns the returned shader.
    /// </summary>
    public SKShader Build()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _owner.Build(Uniforms, Children);
    }

    internal bool IsOwnedBy(SKSLShader shader)
        => ReferenceEquals(_owner, shader);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Uniforms.Dispose();
        Children.Dispose();
    }
}
