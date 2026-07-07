using Beutl.Configuration;
using Beutl.Language;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

public sealed class ElementResizeService : IElementResizeService
{
    private readonly HistoryManager _historyManager;

    public ElementResizeService(HistoryManager historyManager)
    {
        _historyManager = historyManager ?? throw new ArgumentNullException(nameof(historyManager));
    }

    public void Resize(Scene scene, IReadOnlyList<ElementResizeRequest> requests, bool ripple = false)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(requests);
        if (requests.Count == 0) return;

        var oldEnds = ripple ? new Dictionary<Element, (int ZIndex, TimeSpan End)>(requests.Count) : null;
        if (ripple)
        {
            foreach (ElementResizeRequest req in requests)
            {
                ValidateRippleRequest(req);
                oldEnds![req.Element] = (req.Element.ZIndex, req.Element.Range.End);
            }
        }

        if (ripple)
        {
            // MoveChild reverts on follower overlap; ripple needs that overlap, and direct
            // writes are still CoreObjectOperationObserver-recorded for undo.
            bool autoAdjust = GlobalConfiguration.Instance.EditorConfig.AutoAdjustSceneDuration;
            foreach (ElementResizeRequest req in requests)
            {
                req.Element.ZIndex = req.ZIndex;
                req.Element.Start = req.NewStart;
                req.Element.Length = req.NewLength;
                if (autoAdjust && scene.Duration + scene.Start < req.Element.Range.End)
                {
                    scene.Duration = req.Element.Range.End - scene.Start;
                }
            }
        }
        else
        {
            foreach (ElementResizeRequest req in requests)
            {
                scene.MoveChild(req.ZIndex, req.NewStart, req.NewLength, req.Element);
            }
        }

        if (ripple)
        {
            Element[] resized = requests.Select(r => r.Element).ToArray();
            foreach (ElementResizeRequest req in requests)
            {
                (int oldZ, TimeSpan oldEnd) = oldEnds![req.Element];
                if (req.Element.ZIndex != oldZ) continue;

                TimeSpan newEnd = req.Element.Range.End;
                TimeSpan delta = newEnd - oldEnd;
                RippleHelper.ShiftAfter(scene, oldZ, oldEnd, delta, resized);
            }
        }

        _historyManager.Commit(CommandNames.MoveElement);
    }

    private static void ValidateRippleRequest(ElementResizeRequest req)
    {
        ArgumentNullException.ThrowIfNull(req.Element);

        if (req.NewStart < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ElementResizeRequest.NewStart));
        }

        if (req.NewLength <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ElementResizeRequest.NewLength));
        }
    }
}
