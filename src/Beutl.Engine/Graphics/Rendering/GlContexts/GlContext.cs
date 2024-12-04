using OpenTK;
using SkiaSharp;

namespace Beutl.Graphics.Rendering.GlContexts;

internal abstract class GlContext : IDisposable, IBindingsContext
{
    public abstract void MakeCurrent();

    public abstract void SwapBuffers();

    public abstract void Destroy();

    public abstract GRGlTextureInfo CreateTexture(SKSizeI textureSize);

    public abstract void DestroyTexture(uint texture);

    public abstract IntPtr GetProcAddress(string procName);

    void IDisposable.Dispose() => Destroy();
}
