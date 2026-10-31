using System.Collections.Immutable;
using Beutl.Audio;
using Beutl.Composition;
using Beutl.Editor.Components.Helpers;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Transformation;
using Beutl.Helpers;
using Beutl.Media.Decoding;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.Threading;
using Microsoft.Extensions.Logging;

namespace Beutl.ViewModels;

internal sealed partial class ElementAdderImpl
{
    private Element CreateElement(ElementDescription description)
    {
        _logger.LogDebug(
            "Creating an element with start {Start}, length {Length}, and layer {Layer}.",
            description.Start,
            description.Length,
            description.Layer);
        return new Element
        {
            Start = { Value = description.Start },
            Length = { Value = description.Length
                     ?? throw new InvalidOperationException("A non-template element source requires a length.") },
            ZIndex = { Value = description.Layer },
        };
    }

    private Element CreateElementFor<TValue>(
        ElementDescription description,
        string fileName,
        out TValue value)
        where TValue : EngineObject, new()
    {
        value = new TValue();
        return CreateElementFor(
            description,
            value,
            string.IsNullOrWhiteSpace(description.Name)
                ? Path.GetFileName(fileName)
                : description.Name);
    }

    // A new element named name, colored after the object's type, that holds the object.
    private Element CreateElementFor(ElementDescription description, EngineObject value, string name)
    {
        Element element = CreateElement(description);
        element.Name.Value = name;
        Type type = value.GetType();
        element.AccentColor.Value = ElementAccentColorGenerator.GenerateColor(type.FullName ?? type.Name);
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

        Transform? transform = drawable.Transform.Value;
        AddOrSetHelper.AddOrSet(ref transform, new TranslateTransform(position));
        drawable.Transform.Value = transform;
    }

    private static T? TrySetDuration<T>(Element element, Func<T> initialize, Func<T, MediaTime?> getDuration)
    {
        T? state = default;
        try
        {
            state = initialize();
            // A missing or temporarily unreadable source reports no duration. Keep the
            // requested length so the clip stays visible and can be resized once it is readable.
            if (getDuration(state) is { } duration)
            {
                element.Length.Value = duration;
            }

            return state;
        }
        catch
        {
            if (state is IDisposable disposable)
                RenderThread.Dispatcher.Dispatch(disposable.Dispose, DispatchPriority.Low);
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

    // Sets the element's length from the resource and keeps the resource until the batch is released.
    private static void TrackDurationResource<TResource>(
        Element element,
        Func<TResource> initialize,
        Func<TResource, MediaTime?> getDuration,
        List<ElementMaterializationResource> resources)
        where TResource : class, IDisposable
    {
        TResource? resource = TrySetDuration(element, initialize, getDuration);
        if (DisposeResourceOnRenderThread(resource) is { } disposal)
        {
            resources.Add(ElementMaterializationResource.Temporary(disposal));
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

    private MediaProbe ProbeMediaFile(string filePath)
    {
        try
        {
            // Both streams, so a video without audio still opens and reports no audio.
            using var reader = MediaReader.Open(filePath, new MediaOptions(MediaMode.AudioVideo));
            return new MediaProbe(MediaProbeState.Opened, reader.HasVideo, reader.HasAudio);
        }
        catch (UnsupportedMediaException ex)
        {
            // A decoder reports "unsupported" when it cannot open a file that exists,
            // but that also covers transient failures such as an exclusive lock. Verify
            // the file is readable before treating the failure as permanent.
            if (!CanReadFile(filePath))
            {
                _logger.LogWarning(
                    ex,
                    "The file '{File}' is temporarily unreadable; importing it as offline media.",
                    filePath);
                return new MediaProbe(MediaProbeState.Unavailable, false, false);
            }

            _logger.LogWarning(ex, "The file '{File}' exists but could not be decoded.", filePath);
            return new MediaProbe(MediaProbeState.Unsupported, false, false);
        }
        catch (Exception ex)
        {
            // Missing or temporarily unreachable media is imported as an offline
            // placeholder; the requested length is kept until it becomes readable.
            _logger.LogWarning(ex, "Failed to open '{File}'; importing it as offline media.", filePath);
            return new MediaProbe(MediaProbeState.Unavailable, false, false);
        }
    }

    private static bool CanReadFile(string filePath)
    {
        try
        {
            using var stream = new FileStream(
                filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    private static bool MatchFileImage(string filePath)
        => MatchFileExtensions(filePath, SharedFilePickerOptions.ImagePatterns);

    private sealed class EngineObjectSourceHandler(ElementAdderImpl owner) : IElementSourceHandler
    {
        public Type SourceType => typeof(ElementSource.EngineObject);

        public ValueTask<ElementSourcePreflightResult> PreflightAsync(
            ElementSourcePreflightContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (context.Description.Length is null)
            {
                return ValueTask.FromResult(ElementSourcePreflightResult.Rejected(
                    new ElementSourcePreflightFailure(
                        "An engine-object element source requires a requested length.")));
            }

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

            Element element = owner.CreateElementFor(
                context.Description,
                engineObject,
                context.Description.ResolveName(engineObject.GetType()));
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

            element.Start.Value = context.Description.Start;
            if (context.Description.Length is { } length)
            {
                element.Length.Value = length;
            }
            element.ZIndex.Value = context.Description.Layer;
            if (!string.IsNullOrWhiteSpace(context.Description.Name))
            {
                element.Name.Value = context.Description.Name;
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

    // Places a set the way pasting several elements does: together, at the free place nearest the request.
    private sealed class ElementSetTemplateSourceHandler : IElementSourceHandler
    {
        public Type SourceType => typeof(ElementSource.ElementSetTemplate);

        public ValueTask<ElementSourcePreflightResult> PreflightAsync(
            ElementSourcePreflightContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ElementDescription description = context.Description;
            if (description.Length is not null
                || description.Position is not null
                || !string.IsNullOrWhiteSpace(description.Name))
            {
                return ValueTask.FromResult(ElementSourcePreflightResult.Rejected(
                    new ElementSourcePreflightFailure(
                        "An element-set template keeps the names, lengths and positions of its elements.")));
            }

            // Refused as a paste onto a locked layer is, rather than moved off it by the search.
            if (context.Scene.IsLayerLocked(description.Layer))
            {
                return ValueTask.FromResult(ElementSourcePreflightResult.Rejected(
                    new LockedElementLayerFailure(description.Layer)));
            }

            ElementSet set;
            try
            {
                set = ((ElementSource.ElementSetTemplate)description.Source).Factory()
                    ?? throw new InvalidOperationException("The element-set template factory returned null.");
            }
            catch (Exception ex)
            {
                return ValueTask.FromResult(ElementSourcePreflightResult.Rejected(
                    new ElementSourcePreflightFailure("The element-set template factory failed.", ex)));
            }

            if (set.Elements.Count == 0)
            {
                return ValueTask.FromResult(ElementSourcePreflightResult.Rejected(
                    new ElementSourcePreflightFailure("The element-set template holds no elements.")));
            }

            (Beutl.Media.TimeRange range, int minZIndex, _) = DuplicateHelper.ComputePlacementRange([.. set.Elements]);
            HashSet<int> rowOffsets = set.Elements.Select(element => element.ZIndex.Value - minZIndex).ToHashSet();
            HashSet<int> lockedRows = context.Scene.Layers
                .Where(layer => layer.IsLocked.Value)
                .Select(layer => layer.ZIndex.Value)
                .ToHashSet();
            if (ElementPlacementHelper.FindFreeAnchor(
                    context.Scene, range, rowOffsets, lockedRows, description.Start, description.Layer)
                is not { } anchor)
            {
                return ValueTask.FromResult(ElementSourcePreflightResult.Rejected(
                    new ElementSourcePreflightFailure("No free place was found for the element-set template.")));
            }

            return ValueTask.FromResult(ElementSourcePreflightResult.Ready(
                new ElementSetPreflight(set, anchor.Start - range.Start, anchor.ZIndex - minZIndex),
                rowOffsets.Select(offset => anchor.ZIndex + offset)));
        }

        public ValueTask<ElementSourceMaterializationResult> MaterializeAsync(
            ElementSourceMaterializationContext context,
            IElementSourcePreflight preflight,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (preflight is not ElementSetPreflight setPreflight)
            {
                return ValueTask.FromResult(ElementSourceMaterializationResult.Rejected(
                    new InvalidElementMaterializationFailure(
                        "The element-set template handler received preflight state from another handler.")));
            }

            ElementSetFactory.Move(setPreflight.Set, setPreflight.StartOffset, setPreflight.LayerOffset);
            Element[] elements = [.. setPreflight.Set.Elements];

            // The earliest element leads, so the timeline scrolls to where the set begins.
            Element primary = elements
                .OrderBy(element => element.Start.Value)
                .ThenBy(element => element.ZIndex.Value)
                .First();
            return ValueTask.FromResult(ElementSourceMaterializationResult.Materialized(
                new ElementMaterialization(
                    primary,
                    elements.Where(element => element != primary),
                    setPreflight.Set.Groups)));
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
            if (context.Description.Length is null)
            {
                return ElementSourcePreflightResult.Rejected(
                    new ElementSourcePreflightFailure(
                        "A file element source requires a requested length."));
            }

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

            MediaProbe probe = kind == FileSourceKind.Image
                ? new MediaProbe(MediaProbeState.Opened, true, false)
                : await Task.Run(() => owner.ProbeMediaFile(fileName), cancellationToken);
            bool reject = probe.State == MediaProbeState.Unsupported;
            if (probe.State == MediaProbeState.Opened)
            {
                // Containers such as .webm belong to both extension lists. When the
                // opened stream has no video, import the audio that is actually there
                // instead of rejecting a readable file because of its extension.
                if (kind == FileSourceKind.Video && !probe.HasVideo && probe.HasAudio)
                {
                    kind = FileSourceKind.Audio;
                }

                reject = kind switch
                {
                    FileSourceKind.Video => !probe.HasVideo,
                    FileSourceKind.Audio => !probe.HasAudio,
                    _ => false,
                };
            }

            if (reject)
            {
                return ElementSourcePreflightResult.Rejected(
                    new UnsupportedElementSourceFailure(
                        typeof(ElementSource.File),
                        $"The file '{fileName}' exists but no registered media decoder could read its stream."));
            }

            bool hasAudio = kind == FileSourceKind.Video && probe.HasAudio;
            int[] layers = hasAudio
                ? [context.Description.Layer, context.Description.Layer + 1]
                : [context.Description.Layer];
            return ElementSourcePreflightResult.Ready(new FilePreflight(kind, hasAudio), layers);
        }

        public async ValueTask<ElementSourceMaterializationResult> MaterializeAsync(
            ElementSourceMaterializationContext context,
            IElementSourcePreflight preflight,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (preflight is not FilePreflight filePreflight)
            {
                return ElementSourceMaterializationResult.Rejected(
                    new InvalidElementMaterializationFailure(
                        "The file source handler received preflight state from another handler."));
            }

            string fileName = ((ElementSource.File)context.Description.Source).FileName;
            var resources = new List<ElementMaterializationResource>();
            try
            {
                switch (filePreflight.Kind)
                {
                    case FileSourceKind.Image:
                        Element imageElement = owner.CreateElementFor<SourceImage>(
                            context.Description,
                            fileName,
                            out SourceImage sourceImage);
                        sourceImage.Source.Value = ImageSource.Open(fileName);
                        return ElementSourceMaterializationResult.Materialized(
                            new ElementMaterialization(imageElement));

                    case FileSourceKind.Video:
                        Element videoElement = owner.CreateElementFor<SourceVideo>(
                            context.Description,
                            fileName,
                            out SourceVideo sourceVideo);
                        VideoSource video = VideoSource.Open(fileName);
                        sourceVideo.Source.Value = video;
                        TrackDurationResource(
                            videoElement,
                            () => video.ToResource(CompositionContext.Default),
                            resource => !resource.IsOffline && resource.Duration > MediaTime.Zero
                                ? resource.Duration
                                : null,
                            resources);

                        if (!filePreflight.HasAudio)
                        {
                            return ElementSourceMaterializationResult.Materialized(
                                new ElementMaterialization(videoElement, resources: resources));
                        }

                        Element soundElement = owner.CreateElementFor<SourceSound>(
                            context.Description,
                            fileName,
                            out SourceSound sourceSound);
                        soundElement.ZIndex.Value++;
                        owner.BeforeCompanionAudioMaterialization?.Invoke();
                        SoundSource sound = SoundSource.Open(fileName);
                        sourceSound.Source.Value = sound;
                        TrackDurationResource(
                            soundElement,
                            () => sound.ToResource(CompositionContext.Default),
                            resource => resource.Duration > MediaTime.Zero ? resource.Duration : null,
                            resources);

                        return ElementSourceMaterializationResult.Materialized(
                            new ElementMaterialization(
                                videoElement,
                                [soundElement],
                                [ImmutableHashSet.Create(videoElement.Id.Value, soundElement.Id.Value)],
                                resources));

                    case FileSourceKind.Audio:
                        Element audioElement = owner.CreateElementFor<SourceSound>(
                            context.Description,
                            fileName,
                            out SourceSound sourceAudio);
                        SoundSource audio = SoundSource.Open(fileName);
                        sourceAudio.Source.Value = audio;
                        TrackDurationResource(
                            audioElement,
                            () => audio.ToResource(CompositionContext.Default),
                            resource => resource.Duration > MediaTime.Zero ? resource.Duration : null,
                            resources);
                        return ElementSourceMaterializationResult.Materialized(
                            new ElementMaterialization(audioElement, resources: resources));

                    default:
                        return ElementSourceMaterializationResult.Rejected(
                            new UnsupportedElementSourceFailure(typeof(ElementSource.File)));
                }
            }
            catch (Exception materializationFailure)
            {
                List<Exception>? cleanupFailures = null;
                for (int i = resources.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        await resources[i].DisposeAsync();
                    }
                    catch (Exception cleanupFailure)
                    {
                        (cleanupFailures ??= []).Add(cleanupFailure);
                    }
                }

                if (cleanupFailures is null)
                    throw;
                throw new AggregateException([materializationFailure, .. cleanupFailures]);
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

    private enum MediaProbeState
    {
        Opened,
        Unsupported,
        Unavailable,
    }

    private readonly record struct MediaProbe(MediaProbeState State, bool HasVideo, bool HasAudio);

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

    // The set the template created, and how far its elements move to the place found for them.
    private sealed record ElementSetPreflight(
        ElementSet Set,
        MediaTime StartOffset,
        int LayerOffset) : IElementSourcePreflight
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
