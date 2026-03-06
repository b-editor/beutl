using SkiaSharp;

namespace Beutl.Graphics.Effects;

public sealed class SKSLShader : IDisposable
{
    private readonly SKRuntimeEffect _effect;
    private bool _disposed;

    private SKSLShader(SKRuntimeEffect effect)
    {
        _effect = effect;
    }

    public static SKSLShader Create(string sksl)
    {
        SKRuntimeEffect? effect = SKRuntimeEffect.CreateShader(sksl, out string? errorText);
        if (effect == null || errorText != null)
        {
            effect?.Dispose();
            throw new InvalidOperationException($"Failed to compile SKSL shader: {errorText}");
        }

        return new SKSLShader(effect);
    }

    public static bool TryCreate(string sksl, out SKSLShader? shader, out string? errorText)
    {
        shader = null;

        if (string.IsNullOrWhiteSpace(sksl))
        {
            errorText = "SKSL source is empty.";
            return false;
        }

        try
        {
            SKRuntimeEffect? effect = SKRuntimeEffect.CreateShader(sksl, out errorText);
            if (effect == null || errorText != null)
            {
                effect?.Dispose();
                return false;
            }

            shader = new SKSLShader(effect);
            return true;
        }
        catch (Exception ex)
        {
            errorText = ex.Message;
            return false;
        }
    }

    public SKRuntimeEffect Effect
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _effect;
        }
    }

    public SKRuntimeShaderBuilder CreateBuilder()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new SKRuntimeShaderBuilder(_effect);
    }

    public EffectTarget ApplyToNewTarget(CustomFilterEffectContext context, SKRuntimeShaderBuilder builder, Rect bounds)
    {
        var newTarget = context.CreateTarget(bounds);
        try
        {
            using (SKShader finalShader = builder.Build())
            using (var paint = new SKPaint())
            using (var canvas = context.Open(newTarget))
            {
                paint.Shader = finalShader;
                canvas.Clear();
                canvas.Canvas.DrawRect(new SKRect(0, 0, bounds.Width, bounds.Height), paint);
            }

            return newTarget;
        }
        catch
        {
            newTarget.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _effect.Dispose();
    }
}
