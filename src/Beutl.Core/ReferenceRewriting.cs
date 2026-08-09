namespace Beutl;

/// <summary>
/// Provides recursive reference rewriting for values owned by an
/// <see cref="IReferenceRewritable"/> implementation.
/// </summary>
public interface IReferenceRewriteContext
{
    /// <summary>
    /// Rewrites references contained in <paramref name="value"/> while preserving its declared type.
    /// </summary>
    T Rewrite<T>(T value);
}

/// <summary>
/// Represents a value that can rebuild itself after its contained references are rewritten.
/// </summary>
/// <remarks>
/// Implementations are responsible for preserving all non-reference state. The returned value must
/// have the same runtime type as the original value; otherwise the rewrite is ignored.
/// </remarks>
public interface IReferenceRewritable
{
    /// <summary>
    /// Returns an equivalent value whose contained references were processed by
    /// <paramref name="context"/>.
    /// </summary>
    object RewriteReferences(IReferenceRewriteContext context);
}
