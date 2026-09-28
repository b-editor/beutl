using System.Collections.Specialized;
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
        if (DataContext is not GraphEditorViewModel { SelectedView.Value: { } channel } model) return;
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
            new KeyFrameVelocityValues(GetKeyVelocity(first), inInfluence, GetKeyVelocity(first, true), outInfluence),
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
        bool previewed = false;
        bool finished = false;
        var initialValues = flyout.Values;
        var subscriptions = new CompositeDisposable();
        VelocityFlyout = flyout;
        flyout.ValuesChanged += (_, values) =>
        {
            if (finished) return;
            if (DataContext != model || model.SelectedView.Value != channel) { flyout.Hide(); return; }
            previewed = true;
            InvalidateVelocityPreview(model);
            using (model.HistoryManager.SuppressRecording())
            {
                if (values == initialValues) Restore();
                else Apply(values);
            }
            model.RefreshVerticalRange();
            UpdateSelectionAdorner();
        };
        flyout.Closed += (_, _) => Finish();
        model.HistoryManager.BeforeMutation.Subscribe(_ => flyout.Hide()).DisposeWith(subscriptions);
        model.SelectedView.Skip(1).Subscribe(_ => flyout.Hide()).DisposeWith(subscriptions);
        EventHandler selectionChanged = (_, _) => flyout.Hide();
        channel.SelectionChanged += selectionChanged;
        Disposable.Create(() => channel.SelectionChanged -= selectionChanged).DisposeWith(subscriptions);
        NotifyCollectionChangedEventHandler keysChanged = (_, _) => flyout.Hide();
        model.Animation.KeyFrames.CollectionChanged += keysChanged;
        Disposable.Create(() => model.Animation.KeyFrames.CollectionChanged -= keysChanged).DisposeWith(subscriptions);
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
            bool changed = previewed && originals.Any(pair => !SameEasing(pair.Value, pair.Key.Easing));
            try
            {
                if (previewed)
                {
                    InvalidateVelocityPreview(model);
                    using (model.HistoryManager.SuppressRecording()) Restore();
                }
                if (changed && flyout.IsConfirmed && DataContext == model && model.SelectedView.Value == channel)
                    model.HistoryManager.ExecuteInTransaction(() => Apply(flyout.Values), CommandNames.EditKeyFrame);
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
        int rate = model.Scene.FindHierarchicalParent<Project>()?.GetFrameRate() ?? 30;
        TimeSpan start = model.Element?.Start ?? model.Scene.Start;
        TimeSpan end = model.Element?.Range.End ?? model.Scene.Start + model.Scene.Duration;
        buffer.DeleteCache((int)Math.Floor(start.ToFrameNumber(rate)), (int)Math.Ceiling(end.ToFrameNumber(rate)));
    }
}
