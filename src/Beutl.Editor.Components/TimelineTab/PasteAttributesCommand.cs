using System.Reactive;
using Avalonia.Input.Platform;
using Beutl.Editor.Components.Services;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.Logging;
using Beutl.ProjectSystem;
using Beutl.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.TimelineTab;

internal static class PasteAttributesCommand
{
    private static readonly ILogger s_logger = Log.CreateLogger(typeof(PasteAttributesCommand));

    internal static (ElementAttributeKinds Kind, string Label)[] Choices =>
    [
        (ElementAttributeKinds.All, Strings.PasteAttributes_All),
        (ElementAttributeKinds.Transform, GraphicsStrings.Transform),
        (ElementAttributeKinds.VideoEffects, Strings.PasteAttributes_VideoEffects),
        (ElementAttributeKinds.AudioEffects, Strings.PasteAttributes_AudioEffects),
        (ElementAttributeKinds.Animations, Strings.PasteAttributes_Animations)
    ];

    internal static ElementAttributeKinds AvailableKinds(IEditorContext? editor, Element? anchor = null)
    {
        if (editor is not { IsEnabled.Value: true, Object: Scene scene }) return 0;

        ElementAttributeKinds available = 0;
        foreach (Element target in GetTargets(editor, anchor))
            available |= ElementAttributePasteService.AvailableKinds(scene, target);

        return available;
    }

    internal static IObservable<Unit> ObserveTargets(IEditorContext editor, Scene scene)
    {
        var selected = editor.GetRequiredService<IEditorSelection>().SelectedElements;
        return selected.CollectionChangedAsObservable().Select(_ => Unit.Default).StartWith(Unit.Default)
            .Select(_ => selected.Count == 0
                ? Observable.Return(Unit.Default)
                : selected.Select(target => ElementEditability.Observe(target, scene).Select(_ => Unit.Default)
                        .Merge(target.Objects.CollectionChangedAsObservable().Select(_ => Unit.Default)))
                    .CombineLatest().Select(_ => Unit.Default))
            .Switch();
    }

    internal static async Task ExecuteAsync(
        IEditorContext editor, ElementAttributeKinds kind, Element? anchor = null, IClipboard? clipboard = null)
    {
        if (editor is not { IsEnabled.Value: true, Object: Scene scene }) return;

        Element[] targets = GetTargets(editor, anchor);
        if (!targets.Any(target => (ElementAttributePasteService.AvailableKinds(scene, target) & kind) != 0)) return;

        var notifications = editor.GetRequiredService<INotificationService>();
        try
        {
            IClipboardGateway? gateway = AvaloniaClipboardGateway.Resolve(editor, clipboard);
            // Ordinary clip Copy includes the first clip in the single-element format, including when a group is copied.
            string? json = gateway is null ? null : await gateway.TryGetStringAsync(BeutlClipboardFormats.Element);
            if (!editor.IsEnabled.Value) return;

            if (ElementAttributePasteService.ReadSource(json) is not { } source)
            {
                notifications.ShowInformation(Strings.PasteAttributes, MessageStrings.CannotPasteFromClipboard);
                return;
            }

            var history = editor.GetRequiredService<HistoryManager>();
            history.FlushPendingMutations();
            await history.RunAfterEditAsync(() =>
            {
                if (editor.IsEnabled.Value)
                    new ElementAttributePasteService(history).Paste(scene, source, targets, kind);
            });
        }
        catch (Exception ex)
        {
            s_logger.LogError(ex, "Pasting clip attributes failed.");
            notifications.ShowError(Strings.PasteAttributes, ex.Message);
        }
    }

    private static Element[] GetTargets(IEditorContext editor, Element? anchor)
    {
        var selected = editor.GetRequiredService<IEditorSelection>().SelectedElements;
        return anchor is not null && !selected.Contains(anchor) ? [anchor] : [.. selected];
    }
}
