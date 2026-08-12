using System.Collections.Immutable;
using Beutl.Audio;
using Beutl.Composition;
using Beutl.Editor;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Transformation;
using Beutl.Helpers;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Threading;
using Microsoft.Extensions.Logging;

namespace Beutl.ViewModels;

internal sealed class ElementAdderImpl : IElementAdder, IDisposable
{
    private readonly ILogger _logger = Log.CreateLogger<ElementAdderImpl>();
    private readonly EditViewModel _context;
    private readonly ElementSourceHandlerRegistry _sourceHandlers;

    public ElementAdderImpl(EditViewModel context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        _sourceHandlers = new ElementSourceHandlerRegistry(
        [
            new ElementSourceHandlerRegistration(
                new EngineObjectSourceHandler(this),
                order: 0),
            new ElementSourceHandlerRegistration(
                new ElementTemplateSourceHandler(this),
                order: 10),
            new ElementSourceHandlerRegistration(
                new FileSourceHandler(this),
                order: 20),
        ],
        context.ExtensionProvider,
        failure => _logger.LogWarning(
            failure.Exception,
            "Ignoring invalid element source-handler contribution from {ExtensionType}.",
            failure.ExtensionType));
    }

    public IElementSourceHandlerRegistry SourceHandlers => _sourceHandlers;

    public void Dispose() => _sourceHandlers.Dispose();

    public async ValueTask<ElementAddResult> AddAsync(
        IReadOnlyList<ElementDescription> descriptions,
        CancellationToken cancellationToken)
    {
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
                cancellationToken);
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
        Guid sceneId = scene.Id;
        var plans = new List<ElementCreationPlan>(descriptions.Count);
        foreach (ElementDescription description in descriptions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_sourceHandlers.TryAcquire(
                    description.Source.GetType(),
                    out IElementSourceHandlerLease? handlerLease))
            {
                return ElementAddResult.Failed(
                    new UnsupportedElementSourceFailure(description.Source.GetType()),
                    description);
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
                        ex),
                    description);
            }

            if (preflight is null)
            {
                return ElementAddResult.Failed(
                    new ElementSourcePreflightFailure("The element source handler returned no preflight result."),
                    description);
            }
            if (!preflight.IsSuccess)
                return ElementAddResult.Failed(preflight.Failure!, description);
            if (preflight.Preflight is null || preflight.TargetLayers.Count == 0)
            {
                return ElementAddResult.Failed(
                    new ElementSourcePreflightFailure("The element source handler returned an incomplete preflight result."),
                    description);
            }
            preflightLeases.Add(preflight.Preflight);

            foreach (int layer in preflight.TargetLayers)
            {
                if (scene.IsLayerLocked(layer))
                {
                    _logger.LogInformation(
                        "Refusing element batch because target layer {Layer} is locked.",
                        layer);
                    return ElementAddResult.Failed(new LockedElementLayerFailure(layer), description);
                }
            }

            plans.Add(new ElementCreationPlan(
                description,
                handlerLease,
                preflight.Preflight,
                preflight.TargetLayers.ToImmutableHashSet()));
        }

        var preparedElements = new List<Element>(descriptions.Count);
        var itemResults = new List<ElementAddItemResult>(descriptions.Count);
        var groups = new List<ImmutableHashSet<Guid>>();
        var elementReferences = new HashSet<Element>(ReferenceEqualityComparer.Instance);
        var elementIds = _context.Scene.Children
            .Select(element => element.Id)
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
                CleanupStagedFiles(
                    preparedElements.Select(element => element.Uri?.LocalPath),
                    new OperationCanceledException(cancellationToken));
                throw;
            }
            catch (Exception ex)
            {
                CleanupStagedFiles(preparedElements.Select(element => element.Uri?.LocalPath), ex);
                return ElementAddResult.Failed(
                    new ElementMaterializationFailure(
                        $"Materialization failed for element source '{plan.Description.Source.GetType().FullName}'.",
                        ex),
                    plan.Description);
            }

            if (result is null)
            {
                return FailMaterialization(
                    preparedElements,
                    plan.Description,
                    new InvalidElementMaterializationFailure(
                        "The element source handler returned no materialization result."));
            }
            if (!result.IsSuccess)
                return FailMaterialization(preparedElements, plan.Description, result.Failure!);
            if (result.Materialization is not { } materialization)
            {
                return FailMaterialization(
                    preparedElements,
                    plan.Description,
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
                return FailMaterialization(preparedElements, plan.Description, validationFailure);
            }

            try
            {
                foreach (Element element in materialization.Elements)
                {
                    element.Uri = RandomFileNameGenerator.GenerateUri(
                        scene.Uri!,
                        EditorConstants.ElementFileExtension);
                    preparedElements.Add(element);
                }
            }
            catch (Exception ex)
            {
                CleanupStagedFiles(preparedElements.Select(element => element.Uri?.LocalPath), ex);
                return ElementAddResult.Failed(
                    new InvalidElementMaterializationFailure(
                        $"The materialized elements could not apply their request metadata: {ex.Message}"),
                    plan.Description);
            }

            groups.AddRange(materialization.Groups.Select(group => group.ToImmutableHashSet()));
            itemResults.Add(new ElementAddItemResult(
                plan.Description,
                materialization.PrimaryElement,
                materialization.CompanionElements));
        }

        string[] stagedFiles = preparedElements
            .Select(element => element.Uri!.LocalPath)
            .ToArray();

        try
        {
            foreach (Element element in preparedElements)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CoreSerializer.StoreToUri(element, element.Uri!);
            }
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            CleanupStagedFiles(stagedFiles, new OperationCanceledException(cancellationToken));
            throw;
        }
        catch (Exception ex)
        {
            CleanupStagedFiles(stagedFiles, ex);
            return ElementAddResult.Failed(new ElementPersistenceFailure(ex));
        }

        CommitValidationFailure? commitValidation = RevalidateBeforeCommit(
            scene,
            sceneId,
            plans,
            itemResults);
        if (commitValidation is not null)
        {
            CleanupStagedFiles(
                stagedFiles,
                commitValidation.Failure.Exception
                ?? new InvalidOperationException(commitValidation.Failure.Message));
            return ElementAddResult.Failed(
                commitValidation.Failure,
                commitValidation.Description);
        }

        try
        {
            foreach (Element element in preparedElements)
            {
                scene.AddChild(element);
            }

            if (groups.Count > 0)
            {
                scene.Groups.AddRange(groups);
            }

            _context.HistoryManager.Commit(CommandNames.AddElement);
        }
        catch (Exception ex)
        {
            try
            {
                _context.HistoryManager.Rollback();
            }
            catch (Exception rollbackException)
            {
                _logger.LogError(
                    rollbackException,
                    "Failed to roll back an unsuccessful element batch mutation caused by {OriginalError}.",
                    ex.Message);
            }

            CleanupStagedFiles(stagedFiles, ex);
            return ElementAddResult.Failed(new ElementSceneMutationFailure(ex));
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
            || scene.Id != expectedSceneId
            || currentScene.Id != expectedSceneId)
        {
            return new CommitValidationFailure(
                new ElementSceneChangedFailure(expectedSceneId, currentScene.Id),
                null);
        }

        for (int index = 0; index < plans.Count; index++)
        {
            ElementCreationPlan plan = plans[index];
            foreach (int layer in plan.TargetLayers)
            {
                if (scene.IsLayerLocked(layer))
                {
                    return new CommitValidationFailure(
                        new LockedElementLayerFailure(layer),
                        plan.Description);
                }
            }

            if (itemResults[index].Elements.Any(element => !plan.TargetLayers.Contains(element.ZIndex)))
            {
                return new CommitValidationFailure(
                    new InvalidElementMaterializationFailure(
                        "A materialized element changed to a layer that was not reserved during preflight."),
                    plan.Description);
            }
        }

        var ids = scene.Children.Select(element => element.Id).ToHashSet();
        foreach (ElementAddItemResult item in itemResults)
        {
            foreach (Element element in item.Elements)
            {
                if (!ids.Add(element.Id))
                {
                    return new CommitValidationFailure(
                        new InvalidElementMaterializationFailure(
                            "Materialized element identifiers must still be unique immediately before commit."),
                        item.Description);
                }
            }
        }

        return null;
    }

    private ElementAddResult FailMaterialization(
        IReadOnlyCollection<Element> preparedElements,
        ElementDescription description,
        ElementAddFailure failure)
    {
        CleanupStagedFiles(
            preparedElements.Select(element => element.Uri?.LocalPath),
            failure.Exception ?? new InvalidOperationException(failure.Message));
        return ElementAddResult.Failed(failure, description);
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
            if (!localIds.Add(element.Id) || !knownElementIds.Add(element.Id))
            {
                return new InvalidElementMaterializationFailure(
                    "Materialized element identifiers must be unique within the scene and batch.");
            }
            if (!targetLayers.Contains(element.ZIndex))
            {
                return new InvalidElementMaterializationFailure(
                    $"Materialized layer {element.ZIndex} was not reserved during preflight.");
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

    private Element CreateElement(Scene scene, ElementDescription description)
    {
        _logger.LogDebug(
            "Creating an element with start {Start}, length {Length}, and layer {Layer}.",
            description.Start,
            description.Length,
            description.Layer);
        return new Element
        {
            Start = description.Start,
            Length = description.Length
                     ?? throw new InvalidOperationException("A non-template element source requires a length."),
            ZIndex = description.Layer,
        };
    }

    private Element CreateElementFor<TValue>(
        Scene scene,
        ElementDescription description,
        string fileName,
        out TValue value)
        where TValue : EngineObject, new()
    {
        Element element = CreateElement(scene, description);
        element.Name = string.IsNullOrWhiteSpace(description.Name)
            ? Path.GetFileName(fileName)
            : description.Name;
        string typeName = typeof(TValue).FullName!;
        element.AccentColor = ColorGenerator.GenerateColor(typeName);

        value = new TValue();
        element.AddObject(value);
        if (value is Drawable drawable)
        {
            SetTransform(drawable, description);
        }

        return element;
    }

    private void SetTransform(Drawable drawable, ElementDescription description)
    {
        if (description.Position is not { } position)
            return;

        Transform? transform = drawable.Transform.CurrentValue;
        AddOrSetHelper.AddOrSet(ref transform, new TranslateTransform(position));
        drawable.Transform.CurrentValue = transform;
    }

    private static T? TrySetDuration<T>(Element element, Func<T> initialize, Func<T, TimeSpan> getDuration)
    {
        try
        {
            T state = initialize();
            element.Length = getDuration(state);
            return state;
        }
        catch
        {
            return default;
        }
    }

    private static IDisposable? DisposeResourceOnRenderThread(IDisposable? resource)
    {
        return resource is null
            ? null
            : Disposable.Create(() =>
                RenderThread.Dispatcher.Dispatch(resource.Dispose, DispatchPriority.Low));
    }

    private void CleanupStagedFiles(IEnumerable<string?> paths, Exception originalException)
    {
        foreach (string path in paths
                     .Where(path => !string.IsNullOrWhiteSpace(path))
                     .Select(path => path!)
                     .Distinct(StringComparer.Ordinal))
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception cleanupException)
            {
                _logger.LogWarning(
                    cleanupException,
                    "Failed to delete staged element file {Path} while handling {OriginalError}.",
                    path,
                    originalException.Message);
            }
        }
    }

    private static bool MatchFileExtensions(string filePath, IEnumerable<string> extensions)
    {
        string ext = Path.GetExtension(filePath);
        return extensions
            .Select(value =>
            {
                int index = value.LastIndexOf('.');
                return index >= 0 ? value[index..] : value;
            })
            .Contains(ext, StringComparer.OrdinalIgnoreCase);
    }

    private static bool MatchFileAudioOnly(string filePath)
        => MatchFileExtensions(
            filePath,
            DecoderRegistry.EnumerateDecoder()
                .SelectMany(decoder => decoder.AudioExtensions())
                .Distinct());

    private static bool MatchFileVideoOnly(string filePath)
        => MatchFileExtensions(
            filePath,
            DecoderRegistry.EnumerateDecoder()
                .SelectMany(decoder => decoder.VideoExtensions())
                .Distinct());

    private bool HasAudioTrack(string filePath)
    {
        try
        {
            using var reader = MediaReader.Open(filePath, new MediaOptions(MediaMode.Audio));
            return reader.HasAudio;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to open the audio stream of '{File}' for track detection; importing as video-only.",
                filePath);
            return false;
        }
    }

    private static bool MatchFileImage(string filePath)
    {
        string[] extensions =
        [
            "*.bmp",
            "*.gif",
            "*.ico",
            "*.jpg",
            "*.jpeg",
            "*.png",
            "*.wbmp",
            "*.webp",
            "*.pkm",
            "*.ktx",
            "*.astc",
            "*.dng",
            "*.heif",
            "*.avif",
        ];
        return MatchFileExtensions(filePath, extensions);
    }

    private sealed class EngineObjectSourceHandler(ElementAdderImpl owner) : IElementSourceHandler
    {
        public Type SourceType => typeof(ElementSource.EngineObject);

        public ValueTask<ElementSourcePreflightResult> PreflightAsync(
            ElementSourcePreflightContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(ElementSourcePreflightResult.Ready(
                StatelessPreflight.Instance,
                [context.Description.Layer]));
        }

        public ValueTask<ElementSourceMaterializationResult> MaterializeAsync(
            ElementSourceMaterializationContext context,
            IElementSourcePreflight preflight,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = (ElementSource.EngineObject)context.Description.Source;
            EngineObject engineObject;
            try
            {
                engineObject = source.Factory()
                    ?? throw new InvalidOperationException("The engine-object factory returned null.");
            }
            catch (Exception ex)
            {
                return ValueTask.FromResult(ElementSourceMaterializationResult.Rejected(
                    new ElementMaterializationFailure("The engine-object factory failed.", ex)));
            }

            Element element = owner.CreateElement(context.Scene, context.Description);
            Type objectType = engineObject.GetType();
            element.Name = context.Description.ResolveName(objectType);
            element.AccentColor = ColorGenerator.GenerateColor(objectType.FullName ?? objectType.Name);
            element.AddObject(engineObject);
            if (engineObject is Drawable drawable)
            {
                owner.SetTransform(drawable, context.Description);
            }

            return ValueTask.FromResult(ElementSourceMaterializationResult.Materialized(
                new ElementMaterialization(element)));
        }
    }

    private sealed class ElementTemplateSourceHandler(ElementAdderImpl owner) : IElementSourceHandler
    {
        public Type SourceType => typeof(ElementSource.ElementTemplate);

        public ValueTask<ElementSourcePreflightResult> PreflightAsync(
            ElementSourcePreflightContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(ElementSourcePreflightResult.Ready(
                StatelessPreflight.Instance,
                [context.Description.Layer]));
        }

        public ValueTask<ElementSourceMaterializationResult> MaterializeAsync(
            ElementSourceMaterializationContext context,
            IElementSourcePreflight preflight,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = (ElementSource.ElementTemplate)context.Description.Source;
            Element element;
            try
            {
                element = source.Factory()
                    ?? throw new InvalidOperationException("The element-template factory returned null.");
            }
            catch (Exception ex)
            {
                return ValueTask.FromResult(ElementSourceMaterializationResult.Rejected(
                    new ElementMaterializationFailure("The element-template factory failed.", ex)));
            }

            element.Start = context.Description.Start;
            if (context.Description.Length is { } length)
            {
                element.Length = length;
            }
            element.ZIndex = context.Description.Layer;
            if (!string.IsNullOrWhiteSpace(context.Description.Name))
            {
                element.Name = context.Description.Name;
            }
            if (context.Description.Position is not null)
            {
                foreach (Drawable drawable in element.Objects.OfType<Drawable>())
                {
                    owner.SetTransform(drawable, context.Description);
                }
            }

            return ValueTask.FromResult(ElementSourceMaterializationResult.Materialized(
                new ElementMaterialization(element)));
        }
    }

    private sealed class FileSourceHandler(ElementAdderImpl owner) : IElementSourceHandler
    {
        public Type SourceType => typeof(ElementSource.File);

        public async ValueTask<ElementSourcePreflightResult> PreflightAsync(
            ElementSourcePreflightContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string fileName = ((ElementSource.File)context.Description.Source).FileName;
            FileSourceKind kind = MatchFileImage(fileName)
                ? FileSourceKind.Image
                : MatchFileVideoOnly(fileName)
                    ? FileSourceKind.Video
                    : MatchFileAudioOnly(fileName)
                        ? FileSourceKind.Audio
                        : FileSourceKind.Unsupported;
            if (kind == FileSourceKind.Unsupported)
            {
                return ElementSourcePreflightResult.Rejected(
                    new UnsupportedElementSourceFailure(
                        typeof(ElementSource.File),
                        $"The file '{fileName}' is not supported by a registered media decoder."));
            }

            bool hasAudio = kind == FileSourceKind.Video
                && await Task.Run(() => owner.HasAudioTrack(fileName), cancellationToken);
            int[] layers = hasAudio
                ? [context.Description.Layer, context.Description.Layer + 1]
                : [context.Description.Layer];
            return ElementSourcePreflightResult.Ready(new FilePreflight(kind, hasAudio), layers);
        }

        public ValueTask<ElementSourceMaterializationResult> MaterializeAsync(
            ElementSourceMaterializationContext context,
            IElementSourcePreflight preflight,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (preflight is not FilePreflight filePreflight)
            {
                return ValueTask.FromResult(ElementSourceMaterializationResult.Rejected(
                    new InvalidElementMaterializationFailure(
                        "The file source handler received preflight state from another handler.")));
            }

            string fileName = ((ElementSource.File)context.Description.Source).FileName;
            var resources = new List<ElementMaterializationResource>();
            switch (filePreflight.Kind)
            {
                case FileSourceKind.Image:
                    Element imageElement = owner.CreateElementFor<SourceImage>(
                        context.Scene,
                        context.Description,
                        fileName,
                        out SourceImage sourceImage);
                    sourceImage.Source.CurrentValue = ImageSource.Open(fileName);
                    return ValueTask.FromResult(ElementSourceMaterializationResult.Materialized(
                        new ElementMaterialization(imageElement)));

                case FileSourceKind.Video:
                    Element videoElement = owner.CreateElementFor<SourceVideo>(
                        context.Scene,
                        context.Description,
                        fileName,
                        out SourceVideo sourceVideo);
                    VideoSource video = VideoSource.Open(fileName);
                    sourceVideo.Source.CurrentValue = video;
                    VideoSource.Resource? videoResource = TrySetDuration(
                        videoElement,
                        () => video.ToResource(CompositionContext.Default),
                        resource => resource.Duration);
                    if (DisposeResourceOnRenderThread(videoResource) is { } videoDisposal)
                    {
                        resources.Add(ElementMaterializationResource.Temporary(videoDisposal));
                    }

                    if (!filePreflight.HasAudio)
                    {
                        return ValueTask.FromResult(ElementSourceMaterializationResult.Materialized(
                            new ElementMaterialization(videoElement, resources: resources)));
                    }

                    Element soundElement = owner.CreateElementFor<SourceSound>(
                        context.Scene,
                        context.Description,
                        fileName,
                        out SourceSound sourceSound);
                    soundElement.ZIndex++;
                    SoundSource sound = SoundSource.Open(fileName);
                    sourceSound.Source.CurrentValue = sound;
                    SoundSource.Resource? soundResource = TrySetDuration(
                        soundElement,
                        () => sound.ToResource(CompositionContext.Default),
                        resource => resource.Duration);
                    if (DisposeResourceOnRenderThread(soundResource) is { } soundDisposal)
                    {
                        resources.Add(ElementMaterializationResource.Temporary(soundDisposal));
                    }

                    return ValueTask.FromResult(ElementSourceMaterializationResult.Materialized(
                        new ElementMaterialization(
                            videoElement,
                            [soundElement],
                            [ImmutableHashSet.Create(videoElement.Id, soundElement.Id)],
                            resources)));

                case FileSourceKind.Audio:
                    Element audioElement = owner.CreateElementFor<SourceSound>(
                        context.Scene,
                        context.Description,
                        fileName,
                        out SourceSound sourceAudio);
                    SoundSource audio = SoundSource.Open(fileName);
                    sourceAudio.Source.CurrentValue = audio;
                    SoundSource.Resource? audioResource = TrySetDuration(
                        audioElement,
                        () => audio.ToResource(CompositionContext.Default),
                        resource => resource.Duration);
                    if (DisposeResourceOnRenderThread(audioResource) is { } audioDisposal)
                    {
                        resources.Add(ElementMaterializationResource.Temporary(audioDisposal));
                    }
                    return ValueTask.FromResult(ElementSourceMaterializationResult.Materialized(
                        new ElementMaterialization(audioElement, resources: resources)));

                default:
                    return ValueTask.FromResult(ElementSourceMaterializationResult.Rejected(
                        new UnsupportedElementSourceFailure(typeof(ElementSource.File))));
            }
        }
    }

    private enum FileSourceKind
    {
        Unsupported,
        Image,
        Video,
        Audio,
    }

    private sealed record StatelessPreflight : IElementSourcePreflight
    {
        public static StatelessPreflight Instance { get; } = new();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed record FilePreflight(
        FileSourceKind Kind,
        bool HasAudio) : IElementSourcePreflight
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed record ElementCreationPlan(
        ElementDescription Description,
        IElementSourceHandlerLease HandlerLease,
        IElementSourcePreflight Preflight,
        IReadOnlySet<int> TargetLayers);

    private sealed record CommitValidationFailure(
        ElementAddFailure Failure,
        ElementDescription? Description);
}
