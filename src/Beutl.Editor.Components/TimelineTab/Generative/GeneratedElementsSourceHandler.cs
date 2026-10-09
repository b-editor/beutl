using Beutl.Editor.Services;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Components.TimelineTab.Generative;

/// <summary>
/// Adds elements a timeline generation has built through the editor's adder, so they get
/// its layer checks, saved element files and single undo step like any other addition.
/// The first element goes on the description's layer and each companion on the next one up.
/// </summary>
internal sealed class GeneratedElementsSourceHandler : IElementSourceHandler
{
    public Type SourceType => typeof(GeneratedElementsSource);

    public ValueTask<ElementSourcePreflightResult> PreflightAsync(
        ElementSourcePreflightContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var source = (GeneratedElementsSource)context.Description.Source;
        int layer = context.Description.Layer;
        return ValueTask.FromResult(ElementSourcePreflightResult.Ready(
            Preflight.Instance,
            Enumerable.Range(layer, source.LayerSpan)));
    }

    public ValueTask<ElementSourceMaterializationResult> MaterializeAsync(
        ElementSourceMaterializationContext context,
        IElementSourcePreflight preflight,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var source = (GeneratedElementsSource)context.Description.Source;
        ElementMaterialization materialization;
        try
        {
            materialization = source.Build();
        }
        catch (Exception ex)
        {
            return ValueTask.FromResult(ElementSourceMaterializationResult.Rejected(
                new ElementMaterializationFailure("The generated elements could not be built.", ex)));
        }

        int layer = context.Description.Layer;
        foreach (Element element in materialization.Elements)
        {
            element.Start = context.Description.Start;
            if (context.Description.Length is { } length)
                element.Length = length;
            element.ZIndex = layer++;
        }

        return ValueTask.FromResult(ElementSourceMaterializationResult.Materialized(materialization));
    }

    private sealed class Preflight : IElementSourcePreflight
    {
        public static Preflight Instance { get; } = new();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
