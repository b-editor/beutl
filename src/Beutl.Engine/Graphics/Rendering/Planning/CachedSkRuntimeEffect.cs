using System.Text;
using Beutl.Graphics.Effects;
using SkiaSharp;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// Owns one backend-validated immutable runtime effect. Runtime values live in fresh
/// <see cref="SKRuntimeEffectUniforms"/> and <see cref="SKRuntimeEffectChildren"/> collections for each
/// execution lease, so no binding can leak between frames. A runtime builder cannot be used here because
/// disposing it also disposes the supplied effect.
/// </summary>
internal sealed class CachedSkRuntimeEffect : IDisposable
{
    private CachedSkRuntimeEffect(SKRuntimeEffect effect, int retainedBytes)
    {
        Effect = effect;
        RetainedBytes = retainedBytes;
    }

    public SKRuntimeEffect Effect { get; }

    public int RetainedBytes { get; }

    public static CachedSkRuntimeEffect Create(SkslMergedProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        return Create(program.Source, program.SourceByteCount);
    }

    public static CachedSkRuntimeEffect Create(string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        return Create(source, Encoding.UTF8.GetByteCount(source));
    }

    private static CachedSkRuntimeEffect Create(string source, int retainedBytes)
    {
        SKRuntimeEffect? effect = SKRuntimeEffect.CreateShader(source, out string? errorText);
        if (effect is null || !string.IsNullOrWhiteSpace(errorText))
        {
            effect?.Dispose();
            throw new InvalidOperationException(
                $"SkSL program validation failed: {errorText ?? "the backend returned no program"}");
        }

        return new CachedSkRuntimeEffect(effect, Math.Max(1, retainedBytes));
    }

    public void Dispose() => Effect.Dispose();
}
