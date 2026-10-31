using System.Collections.Immutable;
using Beutl.Editor;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Services;
using Beutl.Logging;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Services;
using Microsoft.Extensions.Logging;

namespace Beutl.ViewModels;

internal sealed partial class ElementAdderImpl : IElementAdder, IAsyncDisposable
{
    private readonly ILogger _logger = Log.CreateLogger<ElementAdderImpl>();
    private readonly EditViewModel _context;
    private readonly ElementSourceHandlerRegistry _sourceHandlers;
    private readonly AsyncOperationLifetime _operations = new();

    // Set only by tests: runs after an imported video's element is created and before its audio opens, so a
    // test can make that step fail.
    internal Action? BeforeCompanionAudioMaterialization { get; set; }

    // Set only by tests: runs just before the history transaction that adds the elements, so a test can
    // cancel at that point.
    internal Action? BeforeHistoryTransaction { get; set; }

    public ElementAdderImpl(EditViewModel context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        _sourceHandlers = new ElementSourceHandlerRegistry(
        [
            new ElementSourceHandlerRegistration(new EngineObjectSourceHandler(this)),
            new ElementSourceHandlerRegistration(new ElementTemplateSourceHandler(this)),
            new ElementSourceHandlerRegistration(new ElementSetTemplateSourceHandler()),
            new ElementSourceHandlerRegistration(new FileSourceHandler(this)),
        ],
        context.ExtensionProvider,
        failure => _logger.LogWarning(
            failure.Exception,
            "Ignoring invalid element source-handler contribution from {ExtensionType}.",
            failure.ExtensionType));
    }

    public async ValueTask DisposeAsync()
    {
        await _operations.DisposeAsync();
        await _sourceHandlers.DisposeAsync();
    }

    public async ValueTask<ElementAddResult> AddAsync(
        IReadOnlyList<ElementDescription> descriptions,
        CancellationToken cancellationToken)
    {
        using AsyncOperationLifetime.Operation operation = _operations.TryEnter()
            ?? throw new ObjectDisposedException(nameof(ElementAdderImpl));
        using CancellationTokenSource linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            operation.CancellationToken);
        CancellationToken operationToken = linkedCancellation.Token;
        var handlerLeases = new List<IElementSourceHandlerLease>();
        var preflightLeases = new List<IElementSourcePreflight>();
        var materializationResources = new List<ElementMaterializationResource>();
        ElementAddResult? result = null;
        try
        {
            result = await AddCoreAsync(
                descriptions,
                handlerLeases,
                preflightLeases,
                materializationResources,
                operationToken);
            return result;
        }
        finally
        {
            try
            {
                await ReleasePipelineResourcesAsync(preflightLeases, materializationResources);
            }
            finally
            {
                ReleaseHandlerLeases(handlerLeases);
            }
        }
    }

    private async ValueTask<ElementAddResult> AddCoreAsync(
        IReadOnlyList<ElementDescription> descriptions,
        ICollection<IElementSourceHandlerLease> handlerLeases,
        ICollection<IElementSourcePreflight> preflightLeases,
        ICollection<ElementMaterializationResource> materializationResources,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(descriptions);
        cancellationToken.ThrowIfCancellationRequested();
        if (descriptions.Count == 0)
            return ElementAddResult.Failed(new EmptyElementAddRequestFailure());

        _logger.LogInformation("Adding {Count} new element descriptions.", descriptions.Count);
        Scene scene = _context.Scene;
        Guid sceneId = scene.Id.Value;
        var plans = new List<ElementCreationPlan>(descriptions.Count);
        ElementAddResult? preflightFailure = await PreflightAsync(
            scene, descriptions, plans, handlerLeases, preflightLeases, cancellationToken);
        if (preflightFailure is not null)
            return preflightFailure;

        var preparedElements = new List<Element>(descriptions.Count);
        var itemResults = new List<ElementAddItemResult>(descriptions.Count);
        var groups = new List<ImmutableHashSet<Guid>>();
        ElementAddResult? materializationFailure = await MaterializeAsync(
            scene, plans, preparedElements, itemResults, groups, materializationResources, cancellationToken);
        if (materializationFailure is not null)
            return materializationFailure;

        // A batch prepared while an edit session is open, such as an AI result, is added after the session ends.
        ElementAddResult? result = null;
        await _context.HistoryManager.RunAfterEditAsync(
            () => result = CommitBatch(scene, sceneId, plans, preparedElements, itemResults, groups, cancellationToken),
            cancellationToken);

        return result!;
    }

    // Acquires a handler and a preflight for every description; returns the failure that rejects the batch.
    private async ValueTask<ElementAddResult?> PreflightAsync(
        Scene scene,
        IReadOnlyList<ElementDescription> descriptions,
        List<ElementCreationPlan> plans,
        ICollection<IElementSourceHandlerLease> handlerLeases,
        ICollection<IElementSourcePreflight> preflightLeases,
        CancellationToken cancellationToken)
    {
        foreach (ElementDescription description in descriptions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_sourceHandlers.TryAcquire(
                    description.Source.GetType(),
                    out IElementSourceHandlerLease? handlerLease))
            {
                return ElementAddResult.Failed(
                    new UnsupportedElementSourceFailure(description.Source.GetType()));
            }
            handlerLeases.Add(handlerLease);
            IElementSourceHandler handler = handlerLease.Handler;

            ElementSourcePreflightResult preflight;
            try
            {
                preflight = await handler.PreflightAsync(
                    new ElementSourcePreflightContext(scene, description),
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                return ElementAddResult.Failed(
                    new ElementSourcePreflightFailure(
                        $"Preflight failed for element source '{description.Source.GetType().FullName}'.",
                        ex));
            }

            if (preflight is null)
            {
                return ElementAddResult.Failed(
                    new ElementSourcePreflightFailure("The element source handler returned no preflight result."));
            }
            if (!preflight.IsSuccess)
                return ElementAddResult.Failed(preflight.Failure!);
            if (preflight.Preflight is null || preflight.TargetLayers.Count == 0)
            {
                return ElementAddResult.Failed(
                    new ElementSourcePreflightFailure("The element source handler returned an incomplete preflight result."));
            }
            preflightLeases.Add(preflight.Preflight);

            foreach (int layer in preflight.TargetLayers)
            {
                if (scene.IsLayerLocked(layer))
                {
                    _logger.LogInformation(
                        "Refusing element batch because target layer {Layer} is locked.",
                        layer);
                    return ElementAddResult.Failed(new LockedElementLayerFailure(layer));
                }
            }

            plans.Add(new ElementCreationPlan(
                description,
                handlerLease,
                preflight.Preflight,
                preflight.TargetLayers.ToImmutableHashSet()));
        }

        return null;
    }

    // Materializes every plan into prepared elements with their URIs; returns the failure that rejects the batch.
    private async ValueTask<ElementAddResult?> MaterializeAsync(
        Scene scene,
        List<ElementCreationPlan> plans,
        List<Element> preparedElements,
        List<ElementAddItemResult> itemResults,
        List<ImmutableHashSet<Guid>> groups,
        ICollection<ElementMaterializationResource> materializationResources,
        CancellationToken cancellationToken)
    {
        var elementReferences = new HashSet<Element>(ReferenceEqualityComparer.Instance);
        var elementIds = _context.Scene.Children
            .Select(element => element.Id.Value)
            .ToHashSet();

        foreach (ElementCreationPlan plan in plans)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ElementSourceMaterializationResult result;
            try
            {
                result = await plan.HandlerLease.Handler.MaterializeAsync(
                    new ElementSourceMaterializationContext(scene, plan.Description),
                    plan.Preflight,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                return ElementAddResult.Failed(
                    new ElementMaterializationFailure(
                        $"Materialization failed for element source '{plan.Description.Source.GetType().FullName}'.",
                        ex));
            }

            if (result is null)
            {
                return ElementAddResult.Failed(
                    new InvalidElementMaterializationFailure(
                        "The element source handler returned no materialization result."));
            }
            if (!result.IsSuccess)
                return ElementAddResult.Failed(result.Failure!);
            if (result.Materialization is not { } materialization)
            {
                return ElementAddResult.Failed(
                    new InvalidElementMaterializationFailure(
                        "The element source handler returned an incomplete materialization result."));
            }
            foreach (ElementMaterializationResource resource in materialization.Resources)
            {
                materializationResources.Add(resource);
            }

            ElementAddFailure? validationFailure = ValidateMaterialization(
                materialization,
                plan.TargetLayers,
                elementReferences,
                elementIds);
            if (validationFailure is not null)
            {
                return ElementAddResult.Failed(validationFailure);
            }

            try
            {
                AssignElementUris(scene, materialization, preparedElements);
            }
            catch (Exception ex)
            {
                return ElementAddResult.Failed(
                    new InvalidElementMaterializationFailure(
                        $"The materialized elements could not apply their request metadata: {ex.Message}"));
            }

            groups.AddRange(materialization.Groups.Select(group => group.ToImmutableHashSet()));
            itemResults.Add(new ElementAddItemResult(
                plan.Description,
                materialization.PrimaryElement,
                materialization.CompanionElements));
        }

        return null;
    }

    // The elements take only their URIs: the next save writes their files, as it does for duplicates.
    private static void AssignElementUris(
        Scene scene, ElementMaterialization materialization, List<Element> preparedElements)
    {
        Uri? sceneUri = scene.Uri;
        string elementDirectory = sceneUri is not null
            ? Path.GetDirectoryName(sceneUri.LocalPath)!
            : UnsavedSceneStorage.GetElementDirectory(scene.Id.Value);
        foreach (Element element in materialization.Elements)
        {
            element.Uri = sceneUri is not null
                ? ElementFileNaming.GetUri(sceneUri, element.Id.Value)
                : RandomFileNameGenerator.GenerateUri(
                    elementDirectory,
                    EditorConstants.ElementFileExtension);
            preparedElements.Add(element);
        }
    }

    private ElementAddResult CommitBatch(
        Scene scene,
        Guid sceneId,
        List<ElementCreationPlan> plans,
        List<Element> preparedElements,
        List<ElementAddItemResult> itemResults,
        List<ImmutableHashSet<Guid>> groups,
        CancellationToken cancellationToken)
    {
        CommitValidationFailure? commitValidation = RevalidateBeforeCommit(
            scene,
            sceneId,
            plans,
            itemResults);
        if (commitValidation is not null)
        {
            return ElementAddResult.Failed(commitValidation.Failure);
        }

        CommitValidationFailure? gatedCommitValidation = null;
        try
        {
            BeforeHistoryTransaction?.Invoke();
            _context.HistoryManager.ExecuteInTransaction(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                gatedCommitValidation = RevalidateBeforeCommit(
                    scene,
                    sceneId,
                    plans,
                    itemResults);
                if (gatedCommitValidation is not null)
                    return;

                foreach (Element element in preparedElements)
                {
                    scene.AddChild(element);
                }

                if (groups.Count > 0)
                {
                    scene.Groups.AddRange(groups);
                }
            }, CommandNames.AddElement, cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ElementAddResult.Failed(new ElementSceneMutationFailure(ex));
        }

        if (gatedCommitValidation is not null)
        {
            return ElementAddResult.Failed(gatedCommitValidation.Failure);
        }

        _logger.LogInformation("Added {Count} elements successfully.", preparedElements.Count);
        return ElementAddResult.Succeeded(itemResults);
    }

    private CommitValidationFailure? RevalidateBeforeCommit(
        Scene scene,
        Guid expectedSceneId,
        IReadOnlyList<ElementCreationPlan> plans,
        IReadOnlyList<ElementAddItemResult> itemResults)
    {
        Scene currentScene = _context.Scene;
        if (!ReferenceEquals(scene, currentScene)
            || scene.Id.Value != expectedSceneId)
        {
            return new CommitValidationFailure(
                new ElementSceneChangedFailure(expectedSceneId, currentScene.Id.Value));
        }

        for (int index = 0; index < plans.Count; index++)
        {
            ElementCreationPlan plan = plans[index];
            foreach (int layer in plan.TargetLayers)
            {
                if (scene.IsLayerLocked(layer))
                {
                    return new CommitValidationFailure(
                        new LockedElementLayerFailure(layer));
                }
            }

            if (itemResults[index].Elements.Any(element => !plan.TargetLayers.Contains(element.ZIndex.Value)))
            {
                return new CommitValidationFailure(
                    new InvalidElementMaterializationFailure(
                        "A materialized element changed to a layer that was not reserved during preflight."));
            }
        }

        var ids = scene.Children.Select(element => element.Id.Value).ToHashSet();
        foreach (ElementAddItemResult item in itemResults)
        {
            foreach (Element element in item.Elements)
            {
                if (!ids.Add(element.Id.Value))
                {
                    return new CommitValidationFailure(
                        new InvalidElementMaterializationFailure(
                            "Materialized element identifiers must still be unique immediately before commit."));
                }
            }
        }

        return null;
    }

    private static ElementAddFailure? ValidateMaterialization(
        ElementMaterialization materialization,
        IReadOnlySet<int> targetLayers,
        ISet<Element> knownElements,
        ISet<Guid> knownElementIds)
    {
        if (materialization.Elements.Count == 0
            || !materialization.Elements.Contains(materialization.PrimaryElement))
        {
            return new InvalidElementMaterializationFailure(
                "A materialization must contain its primary element.");
        }

        var localIds = new HashSet<Guid>();
        foreach (Element element in materialization.Elements)
        {
            if (!knownElements.Add(element))
            {
                return new InvalidElementMaterializationFailure(
                    "A materialized element instance cannot appear more than once in a batch.");
            }
            if (!localIds.Add(element.Id.Value) || !knownElementIds.Add(element.Id.Value))
            {
                return new InvalidElementMaterializationFailure(
                    "Materialized element identifiers must be unique within the scene and batch.");
            }
            if (!targetLayers.Contains(element.ZIndex.Value))
            {
                return new InvalidElementMaterializationFailure(
                    $"Materialized layer {element.ZIndex.Value} was not reserved during preflight.");
            }
        }

        foreach (IReadOnlySet<Guid> group in materialization.Groups)
        {
            if (group.Count < 2 || group.Any(id => !localIds.Contains(id)))
            {
                return new InvalidElementMaterializationFailure(
                    "Materialized groups must contain at least two elements from the same result.");
            }
        }

        return null;
    }

    private async ValueTask ReleasePipelineResourcesAsync(
        IReadOnlyList<IElementSourcePreflight> preflightLeases,
        IReadOnlyList<ElementMaterializationResource> materializationResources)
    {
        List<Exception>? errors = null;
        for (int index = materializationResources.Count - 1; index >= 0; index--)
        {
            ElementMaterializationResource resource = materializationResources[index];
            try
            {
                await resource.DisposeAsync();
            }
            catch (Exception ex)
            {
                (errors ??= []).Add(ex);
            }
        }

        for (int index = preflightLeases.Count - 1; index >= 0; index--)
        {
            try
            {
                await preflightLeases[index].DisposeAsync();
            }
            catch (Exception ex)
            {
                (errors ??= []).Add(ex);
            }
        }

        if (errors is not null)
        {
            foreach (Exception error in errors)
            {
                _logger.LogWarning(error, "An element pipeline resource failed to release.");
            }
        }
    }

    private static void ReleaseHandlerLeases(IReadOnlyList<IElementSourceHandlerLease> handlerLeases)
    {
        for (int index = handlerLeases.Count - 1; index >= 0; index--)
        {
            handlerLeases[index].Dispose();
        }
    }

    private sealed record ElementCreationPlan(
        ElementDescription Description,
        IElementSourceHandlerLease HandlerLease,
        IElementSourcePreflight Preflight,
        IReadOnlySet<int> TargetLayers);

    private sealed record CommitValidationFailure(ElementAddFailure Failure);
}
