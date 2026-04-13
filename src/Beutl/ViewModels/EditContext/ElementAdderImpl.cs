using Beutl.Audio;
using Beutl.Composition;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Transformation;
using Beutl.Helpers;
using Beutl.Language;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Threading;
using Microsoft.Extensions.Logging;

namespace Beutl.ViewModels;

internal sealed class ElementAdderImpl(EditViewModel context) : IElementAdder
{
    private readonly ILogger _logger = Log.CreateLogger<ElementAdderImpl>();
    private readonly EditViewModel _context = context;

    public void AddElement(ElementDescription desc)
    {
        _logger.LogInformation("Adding new element with description: {Description}", desc);

        Scene scene = _context.Scene;

        Element CreateElement()
        {
            _logger.LogDebug("Creating new element with start: {Start}, length: {Length}, layer: {Layer}", desc.Start,
                desc.Length, desc.Layer);
            return new Element()
            {
                Start = desc.Start,
                Length = desc.Length,
                ZIndex = desc.Layer,
                Uri = RandomFileNameGenerator.GenerateUri(scene.Uri!, Constants.ElementFileExtension)
            };
        }

        void SetAccentColor(Element element, string str)
        {
            _logger.LogDebug("Setting accent color for element: {Element}, color string: {ColorString}", element, str);
            element.AccentColor = ColorGenerator.GenerateColor(str);
        }

        void SetTransform(Drawable drawable)
        {
            if (!desc.Position.IsDefault)
            {
                _logger.LogDebug(
                    "Setting transform for drawable: {Drawable}, position: {Position}",
                    drawable, desc.Position);
                Transform? transform = drawable.Transform.CurrentValue;
                AddOrSetHelper.AddOrSet(
                    ref transform,
                    new TranslateTransform(desc.Position));
                drawable.Transform.CurrentValue = transform;
            }
        }

        T? TrySetDuration<T>(Element element, Func<T> init, Func<T, TimeSpan> getDuration)
        {
            try
            {
                var state = init();
                element.Length = getDuration(state);
                return state;
            }
            catch
            {
                return default;
            }
        }

        TimelineTabViewModel? timeline = _context.FindToolTab<TimelineTabViewModel>();
        using var compositeDisposable = new CompositeDisposable();

        if (desc.FileName != null)
        {
            _logger.LogInformation("Adding element from file: {FileName}", desc.FileName);
            (TimeRange Range, int ZIndex)? scrollPos = null;

            Element CreateElementFor<TValue>(out TValue value)
                where TValue : EngineObject, new()
            {
                Element element = CreateElement();
                element.Name = Path.GetFileName(desc.FileName);
                SetAccentColor(element, typeof(TValue).FullName!);

                value = new TValue();
                element.AddObject(value);
                if (value is Drawable drawable)
                {
                    SetTransform(drawable);
                }

                return element;
            }

            if (MatchFileImage(desc.FileName))
            {
                _logger.LogDebug("File is an image.");
                Element element = CreateElementFor<SourceImage>(out var t);
                t.Source.CurrentValue = ImageSource.Open(desc.FileName);

                CoreSerializer.StoreToUri(element, element.Uri!);
                scene.AddChild(element);
                scrollPos = (element.Range, element.ZIndex);
            }
            else if (MatchFileVideoOnly(desc.FileName))
            {
                _logger.LogDebug("File is a video.");
                Element element1 = CreateElementFor<SourceVideo>(out var t1);
                Element element2 = CreateElementFor<SourceSound>(out var t2);
                element2.ZIndex++;
                var video = VideoSource.Open(desc.FileName);
                t1.Source.CurrentValue = video;
                var videoResource = TrySetDuration(
                    element1,
                    () => video.ToResource(CompositionContext.Default),
                    v => v.Duration);

                var sound = SoundSource.Open(desc.FileName);
                t2.Source.CurrentValue = sound;
                var soundResource = TrySetDuration(
                    element2,
                    () => sound.ToResource(CompositionContext.Default),
                    v => v.Duration);
                // VideoSource.Resource, SoundSource.ResourceのMediaReaderは参照カウンターで管理され、Resource間で共有される
                // すぐに解放してしまうとこのDuration設定時とレンダリング時の2回MediaReaderが生成されてしまう
                // 作成 -> 参照カウントを引く -> 解放 -> レンダラ側で作成 のようになってしまう
                // これを以下のようにさせる
                // 作成 -> レンダラ側で参照カウントを追加 -> 以下のDisposeで参照カウントを引く -> 実体は解放されない
                compositeDisposable.Add(Disposable.Create(() => RenderThread.Dispatcher.Dispatch(() =>
                {
                    videoResource?.Dispose();
                    soundResource?.Dispose();
                }, DispatchPriority.Low)));

                CoreSerializer.StoreToUri(element1, element1.Uri!);
                CoreSerializer.StoreToUri(element2, element2.Uri!);
                scene.AddChild(element1);
                scene.AddChild(element2);
                // グループ化
                scene.Groups.Add([element1.Id, element2.Id]);
                scrollPos = (element1.Range, element1.ZIndex);
            }
            else if (MatchFileAudioOnly(desc.FileName))
            {
                _logger.LogDebug("File is an audio.");
                Element element = CreateElementFor<SourceSound>(out var t);
                var sound = SoundSource.Open(desc.FileName);
                t.Source.CurrentValue = sound;
                var soundResource = TrySetDuration(
                    element,
                    () => sound.ToResource(CompositionContext.Default),
                    v => v.Duration);
                compositeDisposable.Add(Disposable.Create(() =>
                    RenderThread.Dispatcher.Dispatch(() => soundResource?.Dispose(), DispatchPriority.Low)));

                CoreSerializer.StoreToUri(element, element.Uri!);
                scene.AddChild(element);
                scrollPos = (element.Range, element.ZIndex);
            }

            _context.HistoryManager.Commit(CommandNames.AddElement);

            if (scrollPos.HasValue && timeline != null)
            {
                _logger.LogDebug("Scrolling to position: {ScrollPosition}", scrollPos.Value);
                timeline?.ScrollTo.Execute(scrollPos.Value);
            }
        }
        else
        {
            _logger.LogInformation("Adding new element without file.");
            Element element = CreateElement();
            if (desc.InitialObject != null)
            {
                element.Name = TypeDisplayHelpers.GetLocalizedName(desc.InitialObject);

                element.AccentColor =
                    ColorGenerator.GenerateColor(desc.InitialObject.FullName ?? desc.InitialObject.Name);
                var engineObject = (EngineObject)Activator.CreateInstance(desc.InitialObject)!;
                element.AddObject(engineObject);
                if (engineObject is Drawable drawable)
                {
                    SetTransform(drawable);
                }
            }

            CoreSerializer.StoreToUri(element, element.Uri!);
            scene.AddChild(element);
            _context.HistoryManager.Commit(CommandNames.AddElement);

            timeline?.ScrollTo.Execute((element.Range, element.ZIndex));
        }

        _logger.LogInformation("Element added successfully.");
    }

    public void AddElementFromTemplate(ObjectTemplateItem template, TimeSpan start, int layer)
    {
        _logger.LogInformation("Adding element from template: {TemplateName}", template.Name.Value);

        Scene scene = _context.Scene;

        ICoreSerializable? instance = template.CreateInstance();
        Element newElement;
        if (instance is Element templateElement)
        {
            // ObjectRegenerator で ID を再生成
            ObjectRegenerator.Regenerate(templateElement, out newElement);

            newElement.Start = start;
            newElement.ZIndex = layer;
        }
        else if (instance is EngineObject templateEngineObject)
        {
            ObjectRegenerator.Regenerate(
                templateEngineObject, templateEngineObject.GetType(), out ICoreSerializable regenerated);
            var newEngineObject = (EngineObject)regenerated;

            newElement = new Element
            {
                Start = start,
                Length = TimeSpan.FromSeconds(5),
                ZIndex = layer,
                Name = template.Name.Value,
                AccentColor = ColorGenerator.GenerateColor(
                    template.ActualType.FullName ?? template.ActualType.Name),
            };
            newElement.AddObject(newEngineObject);
        }
        else
        {
            _logger.LogWarning("Failed to create element from template.");
            return;
        }

        newElement.Uri = RandomFileNameGenerator.GenerateUri(scene.Uri!, Constants.ElementFileExtension);

        CoreSerializer.StoreToUri(newElement, newElement.Uri!);
        scene.AddChild(newElement);
        _context.HistoryManager.Commit(CommandNames.AddElementFromTemplate);

        TimelineTabViewModel? timeline = _context.FindToolTab<TimelineTabViewModel>();
        timeline?.ScrollTo.Execute((newElement.Range, newElement.ZIndex));

        _logger.LogInformation("Element from template added successfully.");
    }

    private static bool MatchFileExtensions(string filePath, IEnumerable<string> extensions)
    {
        string ext = Path.GetExtension(filePath);
        return extensions
            .Select(x =>
            {
                int idx = x.LastIndexOf('.');
                if (0 <= idx)
                    return x.Substring(idx);
                else
                    return x;
            })
            .Contains(ext, StringComparer.OrdinalIgnoreCase);
    }

    private static bool MatchFileAudioOnly(string filePath)
    {
        return MatchFileExtensions(filePath, DecoderRegistry.EnumerateDecoder()
            .SelectMany(x => x.AudioExtensions())
            .Distinct());
    }

    private static bool MatchFileVideoOnly(string filePath)
    {
        return MatchFileExtensions(filePath, DecoderRegistry.EnumerateDecoder()
            .SelectMany(x => x.VideoExtensions())
            .Distinct());
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
}
