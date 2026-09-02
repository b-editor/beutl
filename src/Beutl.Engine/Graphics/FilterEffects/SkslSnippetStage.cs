namespace Beutl.Graphics.Effects;

internal sealed class SkslSnippetStage
{
    public SkslSnippetStage(
        ShaderDescription description,
        SkslCoverageBehavior coverageBehavior = SkslCoverageBehavior.RequiresResolvedCoverage)
    {
        ArgumentNullException.ThrowIfNull(description);
        if (description.Kind is not (ShaderDescriptionKind.CurrentPixel or ShaderDescriptionKind.WholeSource))
        {
            throw new ArgumentException(
                "Only validated CurrentPixel and WholeSource shader descriptions can participate in a snippet run.",
                nameof(description));
        }
        if (!Enum.IsDefined(coverageBehavior))
            throw new ArgumentOutOfRangeException(nameof(coverageBehavior));

        Description = description;
        CoverageBehavior = coverageBehavior;
    }

    public ShaderDescription Description { get; }

    public SkslCoverageBehavior CoverageBehavior { get; }
}
