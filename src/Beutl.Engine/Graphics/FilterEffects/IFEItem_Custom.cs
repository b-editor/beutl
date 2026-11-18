namespace Beutl.Graphics.Effects;

internal interface IFEItem_Custom
{
    CustomFilterEffectOptions Options { get; }

    void Accepts(CustomFilterEffectContext context);

    /// <summary>Whether the callback declares where it reads, so that it may be given only part of a value.</summary>
    bool DeclaresSamplingBounds { get; }

    /// <summary>
    /// Maps a requested part of the output to the part of the input the callback reads to produce it.
    /// </summary>
    /// <returns><see langword="false"/> when the callback does not declare where it reads.</returns>
    /// <remarks>
    /// The input it returns gives targets whose output covers <paramref name="output"/> wherever the callback moves
    /// them, so the region analysis can materialize only that part of the input.
    /// </remarks>
    bool TryTransformSamplingBounds(Rect output, out Rect input);
}
