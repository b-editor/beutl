using Avalonia.Threading;
using Beutl.Editor.Models;
using Beutl.Editor.Services.Captions;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Services.AI;
using Microsoft.Extensions.Logging;

namespace Beutl.ViewModels.Dialogs;

public sealed partial class AiSubtitleDialogViewModel
{
    private readonly object _templatePreviewGate = new();
    private int _templatePreviewAttachments;
    private CancellationTokenSource? _templatePreviewCts;

    internal Action? BeforeTemplatePreviewAdmission { get; set; }

    internal Func<
        IReadOnlyList<Element>,
        Beutl.Media.PixelSize,
        CancellationToken,
        Task<byte[]?>> TemplatePreviewRenderer
    { get; set; }
        = static (elements, frameSize, cancellationToken) =>
            CaptionTemplatePreviewRenderer.RenderPngAsync(
                elements,
                frameSize,
                cancellationToken).AsTask();

    private void RefreshTemplatePreview()
    {
        if (_disposed)
            return;

        if (SelectedSubtitlePageIndex.Value != EditPageIndex)
        {
            CancellationTokenSource? previewCts;
            lock (_templatePreviewGate)
            {
                previewCts = _templatePreviewCts;
                _templatePreviewCts = null;
            }
            CancelTemplatePreview(previewCts);
            ReplaceTemplatePreviewImage(null);
            return;
        }

        string text = SelectedCue.Value?.Text
            ?? _editableCues.FirstOrDefault()?.Text
            ?? Strings.AiSubtitle_PreviewSample;
        TemplatePreviewText.Value = string.IsNullOrWhiteSpace(text)
            ? Strings.AiSubtitle_PreviewSample
            : text;
        TemplatePreviewFontSize.Value = 24;
        CaptionCue cue = SelectedCue.Value?.TryCreateCue(out CaptionCue? selectedCue) == true
            && selectedCue is not null
            ? string.IsNullOrWhiteSpace(selectedCue.Text)
                ? selectedCue with { Text = TemplatePreviewText.Value }
                : selectedCue
            : new CaptionCue(TimeSpan.Zero, TimeSpan.FromSeconds(2), TemplatePreviewText.Value);
        if (Volatile.Read(ref _templatePreviewAttachments) > 0)
            RefreshTemplatePreviewImage(cue);
    }

    internal void AttachTemplatePreview()
    {
        bool refresh;
        lock (_templatePreviewGate)
        {
            if (_disposed)
                return;

            _templatePreviewAttachments++;
            refresh = _templatePreviewAttachments == 1;
        }
        if (refresh)
            RefreshTemplatePreview();
    }

    internal void DetachTemplatePreview()
    {
        CancellationTokenSource? previewCts;
        lock (_templatePreviewGate)
        {
            if (_templatePreviewAttachments <= 0)
                return;

            _templatePreviewAttachments--;
            if (_templatePreviewAttachments > 0)
                return;

            previewCts = _templatePreviewCts;
            _templatePreviewCts = null;
        }

        CancelTemplatePreview(previewCts);
        if (!_disposed)
            ReplaceTemplatePreviewImage(null);
    }

    private void RefreshTemplatePreviewImage(CaptionCue cue)
    {
        BeforeTemplatePreviewAdmission?.Invoke();
        CancellationTokenSource cts = new();
        CancellationTokenSource? previous;
        AsyncOperationLifetime.Operation? operation;
        lock (_templatePreviewGate)
        {
            if (_disposed
                || _templatePreviewAttachments <= 0
                || SelectedSubtitlePageIndex.Value != EditPageIndex)
            {
                cts.Dispose();
                return;
            }

            operation = _operations.TryEnter();
            if (operation is null)
            {
                cts.Dispose();
                return;
            }

            previous = _templatePreviewCts;
            _templatePreviewCts = cts;
        }

        CancelTemplatePreview(previous);
        if (!operation.TryPublish(() => ReplaceTemplatePreviewImage(null))
            // Nothing is selected while the template list is being swapped or is empty.
            || SelectedCaptionTemplate.Value is not { } template)
        {
            lock (_templatePreviewGate)
            {
                if (ReferenceEquals(_templatePreviewCts, cts))
                    _templatePreviewCts = null;
            }
            operation.Dispose();
            cts.Dispose();
            return;
        }
        CaptionTemplateId templateId = template.Id;
        Beutl.Media.PixelSize frameSize = _editViewModel is { } editor
            ? new Beutl.Media.PixelSize(editor.Scene.FrameSize.Width, editor.Scene.FrameSize.Height)
            : new Beutl.Media.PixelSize(1920, 1080);
        _ = RenderTemplatePreviewImageAsync(cue, templateId, frameSize, cts, operation);
    }

    private async Task RenderTemplatePreviewImageAsync(
        CaptionCue cue,
        CaptionTemplateId templateId,
        Beutl.Media.PixelSize frameSize,
        CancellationTokenSource cts,
        AsyncOperationLifetime.Operation operation)
    {
        using AsyncOperationLifetime.Operation ownedOperation = operation;
        using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cts.Token,
            ownedOperation.CancellationToken);
        CancellationToken cancellationToken = linkedCts.Token;
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
            using (ICaptionTemplateLease template = _captionTemplates.Acquire(
                       templateId))
            {
                List<Element>? elements = CreatePreviewElements(template, cue, frameSize, cancellationToken);
                if (elements is null || elements.Count == 0)
                    return;

                byte[]? png = await TemplatePreviewRenderer(
                        elements,
                        frameSize,
                        cancellationToken)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                Ref<Beutl.Media.Bitmap>? image = null;
                if (png is { Length: > 0 })
                {
                    using var stream = new MemoryStream(png, writable: false);
                    image = Ref<Beutl.Media.Bitmap>.Create(Beutl.Media.Bitmap.FromStream(stream));
                }

                bool transferred = false;
                try
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (!cancellationToken.IsCancellationRequested
                            && SelectedSubtitlePageIndex.Value == EditPageIndex
                            && ownedOperation.TryPublish(() => ReplaceTemplatePreviewImage(image)))
                        {
                            transferred = true;
                        }
                    });
                }
                finally
                {
                    if (!transferred)
                        image?.Dispose();
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to render a subtitle template preview.");
        }
        finally
        {
            lock (_templatePreviewGate)
            {
                if (ReferenceEquals(_templatePreviewCts, cts))
                    _templatePreviewCts = null;
            }
            cts.Dispose();
        }
    }

    // The elements the template makes for cue, placed as the preview frame shows them; null when
    // the template describes a source the preview cannot build.
    private static List<Element>? CreatePreviewElements(
        ICaptionTemplateLease template,
        CaptionCue cue,
        Beutl.Media.PixelSize frameSize,
        CancellationToken cancellationToken)
    {
        var elements = new List<Element>();
        CaptionElementContext context = new(
            0,
            Strings.AiSubtitle,
            new Beutl.Graphics.Point(
                0,
                frameSize.Height * 0.35f));
        foreach (ElementDescription description in template.CreateElements(cue, context))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Element element;
            switch (description.Source)
            {
                case ElementSource.EngineObject source:
                    element = new Element
                    {
                        Start = description.Start,
                        Length = description.Length ?? TimeSpan.FromSeconds(2),
                        ZIndex = description.Layer,
                    };
                    element.AddObject(source.Factory());
                    break;
                case ElementSource.ElementTemplate source:
                    element = source.Factory()
                        ?? throw new InvalidOperationException("The element-template factory returned null.");
                    element.Start = description.Start;
                    if (description.Length is { } length)
                        element.Length = length;
                    element.ZIndex = description.Layer;
                    break;
                default:
                    return null;
            }

            if (description.Position is { } position)
            {
                foreach (Beutl.Graphics.Drawable drawable in element.Objects.OfType<Beutl.Graphics.Drawable>())
                {
                    Beutl.Graphics.Transformation.Transform? transform = drawable.Transform.CurrentValue;
                    Beutl.Helpers.AddOrSetHelper.AddOrSet(
                        ref transform,
                        new Beutl.Graphics.Transformation.TranslateTransform(position));
                    drawable.Transform.CurrentValue = transform;
                }
            }

            elements.Add(element);
        }
        return elements;
    }

    private void StopTemplatePreviewAdmission()
        => CancelTemplatePreview(CloseTemplatePreviewAdmission());

    private CancellationTokenSource? CloseTemplatePreviewAdmission()
    {
        lock (_templatePreviewGate)
        {
            _templatePreviewAttachments = 0;
            CancellationTokenSource? previewCts = _templatePreviewCts;
            _templatePreviewCts = null;
            return previewCts;
        }
    }

    private void ReplaceTemplatePreviewImage(Ref<Beutl.Media.Bitmap>? image)
    {
        Ref<Beutl.Media.Bitmap>? previous = TemplatePreviewImage.Value;
        TemplatePreviewImage.Value = image;
        previous?.Dispose();
    }

    private void CancelTemplatePreview(CancellationTokenSource? cancellation)
    {
        try
        {
            cancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to cancel a superseded subtitle template preview.");
        }
    }
}
