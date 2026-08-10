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
