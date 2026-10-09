using Beutl.Collections;
using Beutl.IO;
using Beutl.Media.Source;
using Beutl.Serialization;

namespace Beutl.ProjectSystem;

/// <summary>
/// How an element's media was made with AI, and every result kept for it, so the element
/// can be generated again under the same conditions or switched back to an earlier take.
/// </summary>
/// <remarks>
/// It is part of the element, so switching takes is an ordinary edit that undo restores,
/// and the record travels with the element when it is copied or the scene is moved.
/// What was asked for is kept as <see cref="Parameters"/>, whose shape belongs to the
/// editor that made the request; the project only stores it.
/// </remarks>
public sealed class ElementGeneration : Hierarchical
{
    public static readonly CoreProperty<string> OperationProperty;
    public static readonly CoreProperty<string> ParametersProperty;
    public static readonly CoreProperty<ImageSource?> InputImageProperty;
    public static readonly CoreProperty<ImageSource?> InputLastFrameProperty;
    public static readonly CoreProperty<VideoSource?> InputVideoProperty;
    public static readonly CoreProperty<Guid> ActiveTakeIdProperty;
    private readonly HierarchicalList<ElementGenerationTake> _takes;

    static ElementGeneration()
    {
        OperationProperty = ConfigureProperty<string, ElementGeneration>(nameof(Operation))
            .Accessor(o => o.Operation, (o, v) => o.Operation = v)
            .DefaultValue(string.Empty)
            .Register();
        ParametersProperty = ConfigureProperty<string, ElementGeneration>(nameof(Parameters))
            .Accessor(o => o.Parameters, (o, v) => o.Parameters = v)
            .DefaultValue("{}")
            .Register();
        InputImageProperty = ConfigureProperty<ImageSource?, ElementGeneration>(nameof(InputImage))
            .Accessor(o => o.InputImage, (o, v) => o.InputImage = v)
            .Register();
        InputLastFrameProperty = ConfigureProperty<ImageSource?, ElementGeneration>(nameof(InputLastFrame))
            .Accessor(o => o.InputLastFrame, (o, v) => o.InputLastFrame = v)
            .Register();
        InputVideoProperty = ConfigureProperty<VideoSource?, ElementGeneration>(nameof(InputVideo))
            .Accessor(o => o.InputVideo, (o, v) => o.InputVideo = v)
            .Register();
        ActiveTakeIdProperty = ConfigureProperty<Guid, ElementGeneration>(nameof(ActiveTakeId))
            .Accessor(o => o.ActiveTakeId, (o, v) => o.ActiveTakeId = v)
            .Register();
    }

    public ElementGeneration()
    {
        _takes = new HierarchicalList<ElementGenerationTake>(this);
    }

    /// <summary>The service operation, such as <c>video.generate</c> or <c>image.edit.restyle</c>.</summary>
    public string Operation
    {
        get;
        set => SetAndRaise(OperationProperty, ref field, value);
    } = string.Empty;

    /// <summary>What was asked for, as a JSON object written by the editor that made the request.</summary>
    public string Parameters
    {
        get;
        set => SetAndRaise(ParametersProperty, ref field, value);
    } = "{}";

    /// <summary>The picture the request started from: the image edited, or a video's first frame.</summary>
    public ImageSource? InputImage
    {
        get;
        set => SetAndRaise(InputImageProperty, ref field, value);
    }

    /// <summary>The frame a generated video was asked to end on.</summary>
    public ImageSource? InputLastFrame
    {
        get;
        set => SetAndRaise(InputLastFrameProperty, ref field, value);
    }

    /// <summary>The clip a video edit or extension started from.</summary>
    public VideoSource? InputVideo
    {
        get;
        set => SetAndRaise(InputVideoProperty, ref field, value);
    }

    /// <summary>Every result kept, oldest first.</summary>
    [NotAutoSerialized]
    public HierarchicalList<ElementGenerationTake> Takes => _takes;

    /// <summary>The take the element currently shows.</summary>
    public Guid ActiveTakeId
    {
        get;
        set => SetAndRaise(ActiveTakeIdProperty, ref field, value);
    }

    public ElementGenerationTake? ActiveTake => _takes.FirstOrDefault(take => take.Id == ActiveTakeId);

    /// <summary>The files this record keeps, so they move and are versioned with the scene.</summary>
    public IEnumerable<IFileSource> EnumerateFileSources()
    {
        if (InputImage is { } image)
            yield return image;
        if (InputLastFrame is { } lastFrame)
            yield return lastFrame;
        if (InputVideo is { } video)
            yield return video;
        foreach (ElementGenerationTake take in _takes)
        {
            if (take.Image is { } takeImage)
                yield return takeImage;
            if (take.Video is { } takeVideo)
                yield return takeVideo;
        }
    }

    public override void Serialize(ICoreSerializationContext context)
    {
        base.Serialize(context);
        if (_takes.Count > 0)
            context.SetValue(nameof(Takes), _takes);
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);
        if (context.GetValue<ElementGenerationTake[]>(nameof(Takes)) is { } takes)
            _takes.Replace(takes);
    }
}

/// <summary>One result kept by an <see cref="ElementGeneration"/>.</summary>
public sealed class ElementGenerationTake : Hierarchical
{
    public static readonly CoreProperty<ImageSource?> ImageProperty;
    public static readonly CoreProperty<VideoSource?> VideoProperty;
    public static readonly CoreProperty<string?> ModelIdProperty;
    public static readonly CoreProperty<int?> SeedProperty;
    public static readonly CoreProperty<string?> SummaryProperty;
    public static readonly CoreProperty<DateTimeOffset> CreatedAtProperty;
    public static readonly CoreProperty<bool> IsOriginalProperty;

    static ElementGenerationTake()
    {
        ImageProperty = ConfigureProperty<ImageSource?, ElementGenerationTake>(nameof(Image))
            .Accessor(o => o.Image, (o, v) => o.Image = v)
            .Register();
        VideoProperty = ConfigureProperty<VideoSource?, ElementGenerationTake>(nameof(Video))
            .Accessor(o => o.Video, (o, v) => o.Video = v)
            .Register();
        ModelIdProperty = ConfigureProperty<string?, ElementGenerationTake>(nameof(ModelId))
            .Accessor(o => o.ModelId, (o, v) => o.ModelId = v)
            .Register();
        SeedProperty = ConfigureProperty<int?, ElementGenerationTake>(nameof(Seed))
            .Accessor(o => o.Seed, (o, v) => o.Seed = v)
            .Register();
        SummaryProperty = ConfigureProperty<string?, ElementGenerationTake>(nameof(Summary))
            .Accessor(o => o.Summary, (o, v) => o.Summary = v)
            .Register();
        CreatedAtProperty = ConfigureProperty<DateTimeOffset, ElementGenerationTake>(nameof(CreatedAt))
            .Accessor(o => o.CreatedAt, (o, v) => o.CreatedAt = v)
            .Register();
        IsOriginalProperty = ConfigureProperty<bool, ElementGenerationTake>(nameof(IsOriginal))
            .Accessor(o => o.IsOriginal, (o, v) => o.IsOriginal = v)
            .Register();
    }

    /// <summary>The picture, for an image edit.</summary>
    public ImageSource? Image
    {
        get;
        set => SetAndRaise(ImageProperty, ref field, value);
    }

    /// <summary>The clip, for a video generation, edit or extension.</summary>
    public VideoSource? Video
    {
        get;
        set => SetAndRaise(VideoProperty, ref field, value);
    }

    public string? ModelId
    {
        get;
        set => SetAndRaise(ModelIdProperty, ref field, value);
    }

    public int? Seed
    {
        get;
        set => SetAndRaise(SeedProperty, ref field, value);
    }

    /// <summary>A short description of what was asked for, shown in the history.</summary>
    public string? Summary
    {
        get;
        set => SetAndRaise(SummaryProperty, ref field, value);
    }

    public DateTimeOffset CreatedAt
    {
        get;
        set => SetAndRaise(CreatedAtProperty, ref field, value);
    }

    /// <summary>
    /// The element's own media from before it was edited, kept so the edit can be taken
    /// back by picking it like any other take.
    /// </summary>
    public bool IsOriginal
    {
        get;
        set => SetAndRaise(IsOriginalProperty, ref field, value);
    }
}
