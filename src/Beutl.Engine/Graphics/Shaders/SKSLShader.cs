using Beutl.Graphics.Effects;
using SkiaSharp;

namespace Beutl.Graphics.Shaders;

public sealed class SKSLShader : IDisposable
{
    private readonly SKRuntimeEffect _effect;
    private bool _disposed;

    private SKSLShader(SKRuntimeEffect effect)
    {
        _effect = effect;
    }

    public static SKSLShader Create(string sksl)
        => Compile(sksl, out string? errorText)
           ?? throw new InvalidOperationException($"Failed to compile SKSL shader: {errorText}");

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
            shader = Compile(sksl, out errorText);
            return shader != null;
        }
        catch (Exception ex)
        {
            errorText = ex.Message;
            return false;
        }
    }

    private static SKSLShader? Compile(string sksl, out string? errorText)
    {
        SKRuntimeEffect? effect = SKRuntimeEffect.CreateShader(sksl, out errorText);
        if (effect == null || errorText != null)
        {
            effect?.Dispose();
            return null;
        }

        return new SKSLShader(effect);
    }

    public SKSLShaderBuilder CreateBuilder()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new SKSLShaderBuilder(this, _effect);
    }

    /// <summary>
    /// Renders a configured runtime shader over the complete backing buffer of an existing target.
    /// The caller retains ownership of <paramref name="target"/>, including when rendering fails.
    /// </summary>
    public void RenderToTarget(
        CustomFilterEffectContext context,
        SKSLShaderBuilder builder,
        EffectTarget target)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(target);
        if (!builder.IsOwnedBy(this))
        {
            throw new ArgumentException(
                "The builder must be created by the shader used for rendering.",
                nameof(builder));
        }
        if (target.RenderTarget is null || target.Scale.IsUnbounded)
            throw new ArgumentException("The target must be materialized with a concrete scale.", nameof(target));

        using SKShader finalShader = builder.Build();
        using var paint = new SKPaint { Shader = finalShader };
        using ImmediateCanvas canvas = context.Open(target);
        canvas.Clear();
        using (canvas.PushDeviceSpace())
        {
            canvas.Canvas.DrawRect(
                SKRect.Create(target.RenderTarget.Width, target.RenderTarget.Height),
                paint);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _effect.Dispose();
    }

    internal SKShader Build(
        SKRuntimeEffectUniforms uniforms,
        SKRuntimeEffectChildren children)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _effect.ToShader(uniforms, children);
    }
}
