using SkiaSharp;

namespace Beutl.Graphics.Effects;

/// <summary>Transfers the single child shader produced by an execution-time resource binder to the renderer.</summary>
/// <remarks>
/// A binder must call <see cref="Set"/> exactly once. The writer is valid only during that binder invocation and
/// must not be retained.
/// </remarks>
public sealed class ShaderResourceWriter
{
    private SKShader? _shader;
    private bool _active = true;

    internal ShaderResourceWriter()
    {
    }

    /// <summary>Sets the binder result and transfers ownership of the shader to the renderer.</summary>
    /// <param name="shader">A non-null, non-disposed shader newly created for this binding invocation.</param>
    /// <remarks>
    /// The renderer disposes <paramref name="shader"/> after binding and program execution, or if binding fails. The
    /// binder must not retain, use, or dispose it after this method returns.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="shader"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException"><paramref name="shader"/> is already disposed.</exception>
    /// <exception cref="InvalidOperationException">The writer is inactive or a shader was already set.</exception>
    public void Set(SKShader shader)
    {
        ThrowIfInactive();
        ArgumentNullException.ThrowIfNull(shader);
        ObjectDisposedException.ThrowIf(shader.Handle == IntPtr.Zero, shader);
        if (_shader is not null)
            throw new InvalidOperationException("A shader resource binder must set its writer exactly once.");
        _shader = shader;
    }

    internal SKShader Complete()
    {
        ThrowIfInactive();
        return _shader
               ?? throw new InvalidOperationException("A shader resource binder must set its writer exactly once.");
    }

    internal void Deactivate() => _active = false;

    internal void DisposePending()
    {
        _shader?.Dispose();
        _shader = null;
    }

    private void ThrowIfInactive()
    {
        if (!_active)
            throw new InvalidOperationException("The shader resource writer is no longer active.");
    }
}
