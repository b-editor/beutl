namespace Beutl.Graphics.Effects;

/// <summary>The recorded operations following a continuation-owning custom effect.</summary>
/// <remarks>
/// Valid only during the owning callback. Applying the suffix does not consume it: it may be
/// applied repeatedly to different targets, or omitted. Nested continuations own their own suffix.
/// The enclosing render request retains every resource referenced by these operations.
/// </remarks>
public sealed class FilterEffectContinuation
{
    private readonly CustomFilterEffectContext _owner;
    private readonly IFEItem[] _items;

    internal FilterEffectContinuation(CustomFilterEffectContext owner, IFEItem[] items)
    {
        _owner = owner;
        _items = items;
    }

    public bool IsEmpty => _items.Length == 0;

    /// <summary>Executes the recorded suffix using the enclosing request's allocation and execution policy.</summary>
    public void ApplyTo(EffectTargets targets) => ApplyTo(targets, _owner.Instance);

    /// <summary>Executes the suffix for an individual input, optionally reevaluating its effects at another time.</summary>
    public void ApplyTo(EffectTargets targets, FilterEffectInstance? instance) => ApplyTo(targets, instance, null);

    // An effect applied after a continuation is recorded at each execution, ahead of the rest of the suffix.
    internal void ApplyTo(EffectTargets targets, FilterEffectInstance? instance, FilterEffect.Resource? head)
    {
        _owner.ThrowIfCallbackEnded();
        ArgumentNullException.ThrowIfNull(targets);
        if (instance is { Count: < 1 })
            throw new ArgumentException("The instance must have a positive count.", nameof(instance));
        using var context = new FilterEffectContext(
            targets.CalculateBounds(), _owner.OutputScale, _owner.WorkingScale);
        if (head is { IsEnabled: true })
            context.ApplyTransactional(head);
        context.AppendItems(_items);
        using var builder = new SKImageFilterBuilder();
        using var executor = _owner.CreateExecutor(targets, builder);
        executor.Instance = instance;
        executor.Apply(context);
        executor.Flush();
    }
}
