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
/// Implementations are responsible for preserving all non-reference state. The rewrite target must
/// have the same runtime type as the source. It is memoized before population so aliases and cycles
/// resolve to the same replacement instance.
/// </remarks>
public interface IReferenceRewritable
{
    /// <summary>
    /// Creates the target that receives rewritten references. Implementations that mutate in place
    /// may return <see langword="this"/>; rebuilding implementations return a shallow target whose
    /// reference-bearing members can be populated by <see cref="RewriteReferences"/>.
    /// </summary>
    IReferenceRewritable CreateReferenceRewriteTarget();

    /// <summary>
    /// Rewrites this target's reference-bearing members through <paramref name="context"/>.
    /// </summary>
    void RewriteReferences(IReferenceRewriteContext context);
}
