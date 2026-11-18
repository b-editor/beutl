namespace Beutl.Graphics.Effects;

internal record FEItem_CustomEffect<T>(
    T Data, Action<T, CustomFilterEffectContext> Action, Func<T, Rect, Rect>? TransformBounds)
    : FEItem<T>(Data, TransformBounds), IFEItem_Custom
{
    public CustomFilterEffectOptions Options { get; init; }

    /// <summary>
    /// Maps a requested part of the output to the part of the input the callback reads for it, or
    /// <see langword="null"/> when the callback does not declare it.
    /// </summary>
    public Func<T, Rect, Rect>? TransformSamplingBounds { get; init; }

    public bool DeclaresSamplingBounds => TransformSamplingBounds is not null;

    public void Accepts(CustomFilterEffectContext context)
    {
        Action.Invoke(Data, context);
    }

    public bool TryTransformSamplingBounds(Rect output, out Rect input)
    {
        if (TransformSamplingBounds is null)
        {
            input = default;
            return false;
        }

        input = TransformSamplingBounds(Data, output);
        return true;
    }
}
