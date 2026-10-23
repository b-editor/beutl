using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Editor.Components.GraphEditorTab.ViewModels;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Services;
using Beutl.ProjectSystem;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Editor.Components.GraphEditorTab.Views;

public partial class GraphEditorView
{
    internal KeyFrameVelocityFlyout? VelocityFlyout { get; private set; }

    internal void ShowVelocityFlyout()
    {
        if (VelocityFlyout?.IsOpen == true) return;
        if (DataContext is not GraphEditorViewModel { CanEdit.Value: true, SelectedView.Value: { } channel } model) return;
        var selection = channel.KeyFrames.Where(x => x.IsSelected.Value).ToArray();
        if (selection.Length == 0) return;
        var first = selection[0];
        int index = channel.KeyFrames.IndexOf(first);
        bool hasIncoming = index > 0;
        bool hasOutgoing = index + 1 < channel.KeyFrames.Count;
        double inInfluence = first.Model.Easing is SplineEasing incoming ? 1 - incoming.X2 : 1d / 3;
        double outInfluence = hasOutgoing && channel.KeyFrames[index + 1].Model.Easing is SplineEasing outgoing
            ? outgoing.X1 : 1d / 3;
        var flyout = new KeyFrameVelocityFlyout(
            new KeyFrameVelocityValues(GraphEditorCurveMath.KeyVelocity(first), inInfluence,
                GraphEditorCurveMath.KeyVelocity(first, true), outInfluence),
            selection.Length > 1 || hasIncoming, selection.Length > 1 || hasOutgoing);
        var originals = new Dictionary<IKeyFrame, Easing>();
        foreach (var key in selection)
        {
            int current = channel.KeyFrames.IndexOf(key);
            if (current > 0) originals.TryAdd(key.Model, key.Model.Easing);
            if (current + 1 < channel.KeyFrames.Count)
            {
                var next = channel.KeyFrames[current + 1].Model;
                originals.TryAdd(next, next.Easing);
            }
        }
        EditSession? session = null;
        bool opening = false;
        bool finished = false;
        var initialValues = flyout.Values;
        var subscriptions = new CompositeDisposable();
        VelocityFlyout = flyout;
        flyout.ValuesChanged += (_, values) =>
        {
            if (finished) return;
            if (DataContext != model || model.SelectedView.Value != channel) { flyout.Hide(); return; }
            if (session is null)
            {
                opening = true;
                try
                {
                    model.HistoryManager.FlushPendingMutations();
                    // Edits the flush ran may have closed the flyout or changed the graph under it.
                    if (!finished && DataContext == model && model.SelectedView.Value == channel
                        && !model.HistoryManager.IsEditing)
                        session = model.HistoryManager.BeginEdit(CommandNames.EditKeyFrame);
                }
                finally { opening = false; }
                if (finished)
                {
                    // Nothing would end a session opened after the flyout closed.
                    session?.Cancel();
                    session = null;
                    return;
                }
                // Another gesture holds history, or the graph changed, so this one cannot preview.
                if (session is null) { flyout.Hide(); return; }
            }
            InvalidateVelocityPreview(model);
            using (session.Enter())
            {
                if (values == initialValues) Restore();
                else Apply(values);
            }
            model.RefreshVerticalRange();
            UpdateSelectionAdorner();
        };
        flyout.Closed += (_, _) => Finish();
        HideOnGraphChange(flyout, model, channel, includeEdits: false, ownMutation: () => opening).DisposeWith(subscriptions);
        try { flyout.ShowAt(VelocityButton); }
        catch { Finish(); throw; }

        void Apply(KeyFrameVelocityValues values)
        {
            foreach (var key in selection)
            {
                int current = channel.KeyFrames.IndexOf(key);
                if (current < 0) continue;
                if (current > 0) SetVelocity(key, true, values.IncomingSpeed, values.IncomingInfluence, replaceEasing: true);
                if (current + 1 < channel.KeyFrames.Count)
                    SetVelocity(channel.KeyFrames[current + 1], false, values.OutgoingSpeed, values.OutgoingInfluence, replaceEasing: true);
            }
        }

        void Restore()
        {
            foreach (var (key, easing) in originals) key.Easing = easing;
        }

        void Finish()
        {
            if (finished) return;
            finished = true;
            subscriptions.Dispose();
            try
            {
                if (session is { IsOpen: true })
                {
                    // Disposing cancels the session unless a confirmed change completed it.
                    using (session)
                    {
                        InvalidateVelocityPreview(model);
                        bool changed = originals.Any(pair => !SameEasing(pair.Value, pair.Key.Easing));
                        if (changed && flyout.IsConfirmed && DataContext == model && model.SelectedView.Value == channel)
                        {
                            using (session.Enter()) Apply(flyout.Values);
                            session.Complete();
                        }
                    }
                }
            }
            finally
            {
                if (VelocityFlyout == flyout) VelocityFlyout = null;
                if (DataContext == model)
                {
                    model.RefreshVerticalRange();
                    UpdateSelectionAdorner();
                    if (flyout.IsConfirmed) Focus();
                }
            }
        }
    }

    private static bool SameEasing(Easing first, Easing second) => ReferenceEquals(first, second)
        || first is SplineEasing a && second is SplineEasing b
            && (a.X1, a.Y1, a.X2, a.Y2) == (b.X1, b.Y1, b.X2, b.Y2);

    private static void InvalidateVelocityPreview(GraphEditorViewModel model)
    {
        if (model.EditorContext.GetService<IBufferStatus>() is not { } buffer) return;
        Rational rate = SceneTimeRangeService.GetFrameRate(model.Scene);
        MediaTime start = model.Element?.Start ?? model.Scene.Start;
        MediaTime end = model.Element?.Range.End ?? model.Scene.Start + model.Scene.Duration;
        buffer.DeleteCache(
            (int)start.ToFrame(rate, MidpointRounding.ToNegativeInfinity), (int)end.ToFrame(rate, MidpointRounding.ToPositiveInfinity));
    }
}
