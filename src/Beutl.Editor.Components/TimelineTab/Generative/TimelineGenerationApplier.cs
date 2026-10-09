using System.Collections.Immutable;
using Beutl.Audio;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Editor.Services.AI;
using Beutl.Graphics;
using Beutl.Graphics.Transformation;
using Beutl.Language;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.NodeGraph.Generative;
using Beutl.ProjectSystem;
using SkiaSharp;

namespace Beutl.Editor.Components.TimelineTab.Generative;

/// <summary>Puts a finished timeline generation's result where the person asked for it.</summary>
internal sealed class TimelineGenerationApplier(
    Scene scene,
    HistoryManager history,
    IElementAdder adder,
    Func<string> resourceDirectory,
    ITimelineMediaProbe probe)
{
    // A result this close to the source's shape replaces it; anything else would be stretched.
    private static readonly double s_shapeTolerance = Math.Log(1.02);

    public async Task ApplyAsync(
        TimelineGenerationTarget target,
        TimelineGenerationSpec spec,
        TimelineGenerationInputs inputs,
        GenerativeRequest request,
        GenerativeExecutionResult result,
        TimelineGenerationSlot? slot,
        IReadOnlyList<TimelineGenerationSlot> reserved,
        CancellationToken cancellationToken)
    {
        string path = result.ResultFile.LocalPath;
        var take = new ElementGenerationTake
        {
            ModelId = result.ModelId,
            Seed = result.Seed,
            Summary = request.Summary,
            CreatedAt = DateTimeOffset.Now,
        };

        if (target.Placement == TimelineGenerationPlacement.NewTake)
        {
            Element element = target.Source ?? throw new InvalidOperationException("A new take needs its element.");
            await AddTakeAsync(element, spec, path, take, cancellationToken);
            return;
        }

        switch (spec.Kind)
        {
            case TimelineGenerationKind.ImageEdit:
                await ApplyImageEditAsync(target, spec, inputs, path, take, reserved, cancellationToken);
                break;
            case TimelineGenerationKind.VideoEdit:
                await ApplyVideoEditAsync(target, spec, inputs, path, take, reserved, cancellationToken);
                break;
            default:
                await ApplyNewClipAsync(target, spec, inputs, path, take, slot, reserved, cancellationToken);
                break;
        }
    }

    /// <summary>
    /// Shows another take of a generated element, as one undoable edit. A clip longer than
    /// the take is shortened to it.
    /// </summary>
    public static void SelectTake(Scene scene, HistoryManager history, Element element, ElementGenerationTake take)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(take);
        if (element.Generation is not { } generation || !generation.Takes.Contains(take))
            throw new ArgumentException("The take does not belong to the element.", nameof(take));
        if (generation.ActiveTakeId == take.Id)
            return;

        history.ExecuteInTransaction(
            () => ShowTake(scene, element, generation, take, duration: null),
            CommandNames.ChangeAiGenerationTake);
    }

    private async Task ApplyNewClipAsync(
        TimelineGenerationTarget target,
        TimelineGenerationSpec spec,
        TimelineGenerationInputs inputs,
        string path,
        ElementGenerationTake take,
        TimelineGenerationSlot? slot,
        IReadOnlyList<TimelineGenerationSlot> reserved,
        CancellationToken cancellationToken)
    {
        (TimeSpan duration, bool hasAudio, TimeSpan? sourceDuration) = await Task.Run(
            () => (ReadDuration(path), probe.HasAudio(path), inputs.VideoPath is { } source ? probe.GetVideoDuration(source) : null),
            cancellationToken);

        // An extension comes back as the whole clip; the element shows only what was added.
        TimeSpan offset = TimeSpan.Zero;
        TimeSpan length = duration;
        if (spec.Kind == TimelineGenerationKind.VideoExtend
            && sourceDuration is { } original
            && duration - original > TimeSpan.FromMilliseconds(100))
        {
            offset = original;
            length = duration - original;
        }

        if (target.Placement == TimelineGenerationPlacement.Gap && target.Gap?.MaxLength is { } max && max < length)
            length = max;

        TimeSpan start = target.Placement switch
        {
            TimelineGenerationPlacement.Gap => target.Gap!.Start,
            _ => target.Source!.Range.End,
        };
        var range = new TimeRange(start, length);
        int span = hasAudio ? 2 : 1;
        int preferred = slot?.Layer ?? target.Gap?.Layer ?? target.Source!.ZIndex;
        int layer = AllFree(preferred, span, range, reserved)
            ? preferred
            : TimelineGenerationSlots.FindFreeLayer(scene, range, preferred, span, reserved)
              ?? throw new GenerativeExecutionException(Strings.AiNoRoomOnTimeline);

        ElementGeneration generation = CreateRecord(spec, inputs, take);
        take.Video = VideoSource.Open(path);
        string name = NameFor(spec);
        Element video = CreateClip(path, name, offset, generation);
        Element? sound = hasAudio ? CreateSound(path, name, offset) : null;
        ElementAddResult added = await adder.AddAsync(
        [
            new ElementDescription(
                start,
                length,
                layer,
                new GeneratedElementsSource(() => sound is null
                    ? new ElementMaterialization(video)
                    : new ElementMaterialization(video, [sound], [ImmutableHashSet.Create(video.Id, sound.Id)]),
                    span),
                name),
        ], cancellationToken);
        ThrowIfFailed(added);
    }

    private async Task ApplyVideoEditAsync(
        TimelineGenerationTarget target,
        TimelineGenerationSpec spec,
        TimelineGenerationInputs inputs,
        string path,
        ElementGenerationTake take,
        IReadOnlyList<TimelineGenerationSlot> reserved,
        CancellationToken cancellationToken)
    {
        Element source = target.Source ?? throw new InvalidOperationException("A video edit needs its clip.");
        // The copy keeps the clip's trim, speed, transform and effects; only its media changes.
        ObjectRegenerator.Regenerate(source, out Element copy);
        SourceVideo video = copy.Objects.OfType<SourceVideo>().FirstOrDefault()
            ?? throw new GenerativeExecutionException(Strings.AiVideoNoSourceSelected);
        video.Source.CurrentValue = VideoSource.Open(path);
        take.Video = VideoSource.Open(path);
        copy.Generation = CreateRecord(spec, inputs, take);
        await AddAboveAsync(source, copy, reserved, cancellationToken);
    }

    private async Task ApplyImageEditAsync(
        TimelineGenerationTarget target,
        TimelineGenerationSpec spec,
        TimelineGenerationInputs inputs,
        string path,
        ElementGenerationTake take,
        IReadOnlyList<TimelineGenerationSlot> reserved,
        CancellationToken cancellationToken)
    {
        Element source = target.Source ?? throw new InvalidOperationException("An image edit needs its picture.");
        string inputPath = inputs.ImagePath ?? throw new GenerativeExecutionException(Strings.AiEditNoSourceSelected);
        (PixelSize original, PixelSize produced) = await Task.Run(
            () => (ReadSize(inputPath), ReadSize(path)),
            cancellationToken);

        SourceImage? image = source.Objects.OfType<SourceImage>().FirstOrDefault();
        if (spec.ImageTask != AiImageEditTask.Upscale
            && image is not null
            && CanReplace(source, image, inputPath, spec)
            && FittedSize(spec, original) is var fitted
            && HasSameShape(produced, fitted))
        {
            string fittedPath = produced == fitted
                ? path
                : await Task.Run(() => Resample(path, fitted), cancellationToken);
            take.Image = ImageSource.Open(fittedPath);
            history.ExecuteInTransaction(() =>
            {
                ElementGeneration generation = CreateRecord(spec, inputs, take);
                // The picture it replaces stays a take, so the edit can be undone by choosing it.
                if (image.Source.CurrentValue is { HasUri: true } previous)
                {
                    generation.Takes.Insert(0, new ElementGenerationTake
                    {
                        Image = ImageSource.Open(previous.Uri.LocalPath),
                        IsOriginal = true,
                        CreatedAt = DateTimeOffset.Now,
                    });
                }

                image.Source.CurrentValue = ImageSource.Open(fittedPath);
                source.Generation = generation;
            }, CommandNames.ApplyAiGeneration);
            return;
        }

        ObjectRegenerator.Regenerate(source, out Element copy);
        SourceImage copyImage = copy.Objects.OfType<SourceImage>().FirstOrDefault()
            ?? throw new GenerativeExecutionException(Strings.AiEditNoSourceSelected);
        copyImage.Source.CurrentValue = ImageSource.Open(path);
        take.Image = ImageSource.Open(path);
        if (spec.ImageTask == AiImageEditTask.Upscale && produced.Width > 0 && original.Width > 0)
        {
            // Keeps the upscaled picture the size the original showed at, with the detail added.
            KeepOnScreenSize(copyImage, original.Width * 100f / produced.Width, original.Height * 100f / produced.Height);
        }

        copy.Generation = CreateRecord(spec, inputs, take);
        await AddAboveAsync(source, copy, reserved, cancellationToken);
    }

    private async Task AddTakeAsync(
        Element element,
        TimelineGenerationSpec spec,
        string path,
        ElementGenerationTake take,
        CancellationToken cancellationToken)
    {
        ElementGeneration generation = element.Generation
            ?? throw new InvalidOperationException("Only a generated element takes another take.");
        TimeSpan? duration = null;
        if (spec.Kind == TimelineGenerationKind.ImageEdit)
        {
            string fittedPath = path;
            if (element.Objects.OfType<SourceImage>().FirstOrDefault()?.Source.CurrentValue is { HasUri: true } shown
                && spec.ImageTask != AiImageEditTask.Upscale)
            {
                (PixelSize current, PixelSize produced) = await Task.Run(
                    () => (ReadSize(shown.Uri.LocalPath), ReadSize(path)),
                    cancellationToken);
                if (current != produced && HasSameShape(produced, current))
                    fittedPath = await Task.Run(() => Resample(path, current), cancellationToken);
            }

            take.Image = ImageSource.Open(fittedPath);
        }
        else
        {
            duration = await Task.Run(() => ReadDuration(path), cancellationToken);
            take.Video = VideoSource.Open(path);
        }

        history.ExecuteInTransaction(() =>
        {
            generation.Takes.Add(take);
            ShowTake(scene, element, generation, take, duration);
        }, CommandNames.ApplyAiGeneration);
    }

    private static void ShowTake(
        Scene scene,
        Element element,
        ElementGeneration generation,
        ElementGenerationTake take,
        TimeSpan? duration)
    {
        if (take.Image is { HasUri: true } picture
            && element.Objects.OfType<SourceImage>().FirstOrDefault() is { } image)
        {
            image.Source.CurrentValue = ImageSource.Open(picture.Uri.LocalPath);
        }
        else if (take.Video is { HasUri: true } clip
                 && element.Objects.OfType<SourceVideo>().FirstOrDefault() is { } video)
        {
            string? previousPath = video.Source.CurrentValue is { HasUri: true } previous ? previous.Uri.LocalPath : null;
            video.Source.CurrentValue = VideoSource.Open(clip.Uri.LocalPath);
            if ((duration ?? TimelineMediaProbe.Instance.GetVideoDuration(clip.Uri.LocalPath)) is { } length)
            {
                TimeSpan available = length - video.OffsetPosition.CurrentValue;
                if (available > TimeSpan.Zero && element.Length > available)
                    element.Length = available;
            }

            // The clip's sound travels in a grouped element of its own; it follows the picture.
            if (previousPath is not null)
            {
                foreach (Element mate in GroupMates(scene, element))
                {
                    foreach (SourceSound sound in mate.Objects.OfType<SourceSound>())
                    {
                        if (sound.Source.CurrentValue is { HasUri: true } heard
                            && string.Equals(heard.Uri.LocalPath, previousPath, StringComparison.Ordinal))
                        {
                            sound.Source.CurrentValue = SoundSource.Open(clip.Uri.LocalPath);
                            mate.Length = element.Length;
                        }
                    }
                }
            }
        }

        generation.ActiveTakeId = take.Id;
    }

    private static IEnumerable<Element> GroupMates(Scene scene, Element element)
    {
        foreach (ImmutableHashSet<Guid> group in scene.Groups)
        {
            if (!group.Contains(element.Id))
                continue;
            foreach (Element other in scene.Children)
            {
                if (!ReferenceEquals(other, element) && group.Contains(other.Id))
                    yield return other;
            }
        }
    }

    private async Task AddAboveAsync(
        Element source,
        Element copy,
        IReadOnlyList<TimelineGenerationSlot> reserved,
        CancellationToken cancellationToken)
    {
        int layer = TimelineGenerationSlots.FindFreeLayer(scene, source.Range, source.ZIndex + 1, reserved: reserved)
            ?? throw new GenerativeExecutionException(Strings.AiNoRoomOnTimeline);
        ElementAddResult added = await adder.AddAsync(
        [
            new ElementDescription(
                source.Start,
                source.Length,
                layer,
                new GeneratedElementsSource(() => new ElementMaterialization(copy), 1),
                copy.Name),
        ], cancellationToken);
        ThrowIfFailed(added);
    }

    private bool AllFree(int layer, int span, TimeRange range, IReadOnlyList<TimelineGenerationSlot> reserved)
    {
        for (int offset = 0; offset < span; offset++)
        {
            if (!TimelineGenerationSlots.IsFree(scene, layer + offset, range, reserved))
                return false;
        }

        return true;
    }

    // Replacing only applies while the element still shows the picture that was edited and
    // nothing would move it: an outpaint grows the picture outward from its centre.
    private bool CanReplace(Element element, SourceImage image, string inputPath, TimelineGenerationSpec spec)
    {
        if (element.IsLocked || scene.IsElementLocked(element) || !scene.Children.Contains(element))
            return false;
        if (image.Source.Animation is not null || image.Source.HasExpression
            || image.Source.CurrentValue is not { HasUri: true } shown
            || !string.Equals(shown.Uri.LocalPath, inputPath, StringComparison.Ordinal))
        {
            return false;
        }

        return spec.ImageTask != AiImageEditTask.Outpaint
            || (image.AlignmentX.CurrentValue == AlignmentX.Center
                && image.AlignmentY.CurrentValue == AlignmentY.Center
                && image.AlignmentX.Animation is null
                && image.AlignmentY.Animation is null);
    }

    private static PixelSize FittedSize(TimelineGenerationSpec spec, PixelSize original)
    {
        if (spec.ImageTask != AiImageEditTask.Outpaint)
            return original;
        (int width, int height, _, _) = AiImageEditTasks.GetOutpaintDimensions(
            original.Width,
            original.Height,
            spec.OutpaintExpansionPercent);
        return new PixelSize(width, height);
    }

    internal static bool HasSameShape(PixelSize produced, PixelSize expected)
        => produced.Width > 0 && produced.Height > 0 && expected.Width > 0 && expected.Height > 0
           && Math.Abs(Math.Log((double)produced.Width / produced.Height)
                       - Math.Log((double)expected.Width / expected.Height)) <= s_shapeTolerance;

    private static void KeepOnScreenSize(Drawable drawable, float scaleX, float scaleY)
    {
        // A group applies its last child first, so the correction scales the picture before
        // the element's own transform places it.
        var correction = new ScaleTransform(scaleX, scaleY);
        switch (drawable.Transform.CurrentValue)
        {
            case TransformGroup group:
                group.Children.Add(correction);
                break;
            case { } single:
                var wrapper = new TransformGroup();
                drawable.Transform.CurrentValue = wrapper;
                wrapper.Children.Add(single);
                wrapper.Children.Add(correction);
                break;
            default:
                drawable.Transform.CurrentValue = correction;
                break;
        }
    }

    private ElementGeneration CreateRecord(
        TimelineGenerationSpec spec,
        TimelineGenerationInputs inputs,
        ElementGenerationTake take)
    {
        var generation = new ElementGeneration
        {
            Operation = spec.OperationId,
            Parameters = spec.ToJson(),
            InputImage = inputs.ImagePath is { } image ? ImageSource.Open(image) : null,
            InputLastFrame = inputs.LastFramePath is { } last ? ImageSource.Open(last) : null,
            InputVideo = inputs.VideoPath is { } video ? VideoSource.Open(video) : null,
        };
        generation.Takes.Add(take);
        generation.ActiveTakeId = take.Id;
        return generation;
    }

    private static Element CreateClip(string path, string name, TimeSpan offset, ElementGeneration generation)
    {
        var element = new Element
        {
            Name = name,
            AccentColor = ColorGenerator.GenerateColor(typeof(SourceVideo).FullName!),
        };
        var video = new SourceVideo();
        video.Source.CurrentValue = VideoSource.Open(path);
        video.OffsetPosition.CurrentValue = offset;
        element.AddObject(video);
        element.Generation = generation;
        return element;
    }

    private static Element CreateSound(string path, string name, TimeSpan offset)
    {
        var element = new Element
        {
            Name = name,
            AccentColor = ColorGenerator.GenerateColor(typeof(SourceSound).FullName!),
        };
        var sound = new SourceSound();
        sound.Source.CurrentValue = SoundSource.Open(path);
        sound.OffsetPosition.CurrentValue = offset;
        element.AddObject(sound);
        return element;
    }

    private static string NameFor(TimelineGenerationSpec spec)
    {
        string prompt = spec.Prompt.Trim();
        return prompt.Length == 0
            ? Strings.AiGeneratedClip
            : prompt.Length <= 40 ? prompt : string.Concat(prompt.AsSpan(0, 39), "…");
    }

    private TimeSpan ReadDuration(string path)
        => probe.GetVideoDuration(path) ?? throw new GenerativeExecutionException(Strings.AiResultUnavailable);

    private static PixelSize ReadSize(string path)
    {
        using var codec = SKCodec.Create(path)
            ?? throw new GenerativeExecutionException(Strings.AiEditSourcePreviewFailed);
        return new PixelSize(codec.Info.Width, codec.Info.Height);
    }

    // Brings the result to the size the replaced picture showed at, next to the scene.
    private string Resample(string path, PixelSize size)
    {
        using SKBitmap source = SKBitmap.Decode(path)
            ?? throw new GenerativeExecutionException(Strings.AiEditSourcePreviewFailed);
        using SKBitmap resized = source.Resize(
            new SKImageInfo(size.Width, size.Height, source.ColorType, source.AlphaType),
            new SKSamplingOptions(SKCubicResampler.Mitchell))
            ?? throw new GenerativeExecutionException(Strings.AiEditSourcePreviewFailed);
        string directory = resourceDirectory();
        Directory.CreateDirectory(directory);
        string destination = Path.Combine(directory, $"{Guid.NewGuid():N}.png");
        using (FileStream stream = File.Create(destination))
        using (SKData data = resized.Encode(SKEncodedImageFormat.Png, 100))
        {
            data.SaveTo(stream);
        }

        return destination;
    }

    private static void ThrowIfFailed(ElementAddResult result)
    {
        if (result.IsSuccess)
            return;
        if (result.Failure is LockedElementLayerFailure)
            throw new GenerativeExecutionException(Strings.LayerIsLocked);
        throw new InvalidOperationException($"Failed to add the generated element: {result.Failure?.Id}.", result.Failure?.Exception);
    }
}

/// <summary>Elements a generation has already built, added as they are.</summary>
internal sealed record GeneratedElementsSource(Func<ElementMaterialization> Build, int LayerSpan) : ElementSource;
