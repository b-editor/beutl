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
using Beutl.Services;
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
            Start = description.Start,
            Length = description.Length
                     ?? throw new InvalidOperationException("A non-template element source requires a length."),
            ZIndex = description.Layer,
        };
    }

    private Element CreateElementFor<TValue>(
        ElementDescription description,
        string fileName,
        out TValue value)
        where TValue : EngineObject, new()
    {
        Element element = CreateElement(description);
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
        T? state = default;
        try
        {
            state = initialize();
            element.Length = getDuration(state);
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
        Func<TResource, TimeSpan> getDuration,
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

            Element element = owner.CreateElement(context.Description);
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

            bool hasAudio = kind == FileSourceKind.Video
                && await Task.Run(() => owner.HasAudioTrack(fileName), cancellationToken);
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
                        sourceImage.Source.CurrentValue = ImageSource.Open(fileName);
                        return ElementSourceMaterializationResult.Materialized(
                            new ElementMaterialization(imageElement));

                    case FileSourceKind.Video:
                        Element videoElement = owner.CreateElementFor<SourceVideo>(
                            context.Description,
                            fileName,
                            out SourceVideo sourceVideo);
                        VideoSource video = VideoSource.Open(fileName);
                        sourceVideo.Source.CurrentValue = video;
                        TrackDurationResource(
                            videoElement,
                            () => video.ToResource(CompositionContext.Default),
                            resource => resource.Duration,
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
                        soundElement.ZIndex++;
                        owner.BeforeCompanionAudioMaterialization?.Invoke();
                        SoundSource sound = SoundSource.Open(fileName);
                        sourceSound.Source.CurrentValue = sound;
                        TrackDurationResource(
                            soundElement,
                            () => sound.ToResource(CompositionContext.Default),
                            resource => resource.Duration,
                            resources);

                        return ElementSourceMaterializationResult.Materialized(
                            new ElementMaterialization(
                                videoElement,
                                [soundElement],
                                [ImmutableHashSet.Create(videoElement.Id, soundElement.Id)],
                                resources));

                    case FileSourceKind.Audio:
                        Element audioElement = owner.CreateElementFor<SourceSound>(
                            context.Description,
                            fileName,
                            out SourceSound sourceAudio);
                        SoundSource audio = SoundSource.Open(fileName);
                        sourceAudio.Source.CurrentValue = audio;
                        TrackDurationResource(
                            audioElement,
                            () => audio.ToResource(CompositionContext.Default),
                            resource => resource.Duration,
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
}
