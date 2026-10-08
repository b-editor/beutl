using System.Collections.Specialized;
using Beutl.Editor.Components.GraphEditorTab.ViewModels;
using Beutl.Language;
using Beutl.Services;

namespace Beutl.Editor.Components.GraphEditorTab.Views;

public partial class GraphEditorView
{
    internal KeyFrameValueScaleFlyout? ValueScaleFlyout { get; private set; }

    internal void DistributeSelectionEvenly()
    {
        if (CaptureSelection(3) is not { } snapshot) return;
        var entries = snapshot.Entries.OrderBy(entry => entry.Time).ToArray();
        double first = entries[0].Time.TotalSeconds;
        double last = entries[^1].Time.TotalSeconds;
        var times = entries.Select((entry, index) =>
                (entry.Model, Time: first + (last - first) * index / (snapshot.Entries.Length - 1)))
            .ToDictionary(entry => entry.Model, entry => entry.Time);
        ApplySelectionTransform(() => snapshot.Apply(entry => (times[entry.Model], entry.Number), updateValues: false));
    }

    internal void ReverseSelection()
    {
        if (CaptureSelection(2) is not { } snapshot) return;
        double ends = snapshot.Entries.Min(entry => entry.Time.TotalSeconds) + snapshot.Entries.Max(entry => entry.Time.TotalSeconds);
        ApplySelectionTransform(() => snapshot.Apply(entry => (ends - entry.Time.TotalSeconds, entry.Number),
            timeScale: -1, transformHandles: true, updateValues: false));
    }

    internal void ScaleSelectionValues(double factor)
    {
        if (!double.IsFinite(factor) || factor == 1 || CaptureSelection(1) is not { } snapshot) return;
        if (snapshot.Entries.Any(entry => !double.IsFinite(entry.Number * factor))) return;
        ApplySelectionTransform(() => snapshot.ScaleValues(factor));
    }

    private GraphEditorDragSnapshot? CaptureSelection(int minimum)
    {
        if (DataContext is not GraphEditorViewModel { IsDisposed: false, IsEditing: false, SelectedView.Value: { } channel }
            || _interactionPointer != null || ControlPointMoveState != null
            || VelocityFlyout?.IsOpen == true || ValueScaleFlyout?.IsOpen == true) return null;
        var snapshot = new GraphEditorDragSnapshot(channel);
        return snapshot.Entries.Length >= minimum ? snapshot : null;
    }

    private void ApplySelectionTransform(Func<bool> apply)
    {
        if (DataContext is not GraphEditorViewModel model) return;
        bool applied = false;
        model.HistoryManager.ExecuteInTransaction(() => applied = apply(), CommandNames.EditKeyFrame);
        if (!applied)
            NotificationService.ShowWarning(Strings.GraphEditor, MessageStrings.GraphKeyFrameTransformRejected);
        model.RefreshVerticalRange();
        UpdateSelectionAdorner();
    }

    internal void ShowValueScaleFlyout()
    {
        if (CaptureSelection(1) == null || DataContext is not GraphEditorViewModel { SelectedView.Value: { } channel } model) return;
        var flyout = new KeyFrameValueScaleFlyout(channel.DisplayName);
        var subscriptions = new CompositeDisposable();
        ValueScaleFlyout = flyout;
        flyout.Closed += (_, _) =>
        {
            subscriptions.Dispose();
            ValueScaleFlyout = null;
            if (flyout.IsConfirmed && ReferenceEquals(DataContext, model) && ReferenceEquals(model.SelectedView.Value, channel))
                ScaleSelectionValues(flyout.Factor);
        };
        model.HistoryManager.BeforeMutation.Subscribe(_ => flyout.Hide()).DisposeWith(subscriptions);
        model.SelectedView.Skip(1).Subscribe(_ => flyout.Hide()).DisposeWith(subscriptions);
        EventHandler changed = (_, _) => flyout.Hide();
        channel.SelectionChanged += changed;
        model.Animation.Edited += changed;
        Disposable.Create(() => channel.SelectionChanged -= changed).DisposeWith(subscriptions);
        Disposable.Create(() => model.Animation.Edited -= changed).DisposeWith(subscriptions);
        NotifyCollectionChangedEventHandler keysChanged = (_, _) => flyout.Hide();
        model.Animation.KeyFrames.CollectionChanged += keysChanged;
        Disposable.Create(() => model.Animation.KeyFrames.CollectionChanged -= keysChanged).DisposeWith(subscriptions);
        try { flyout.ShowAt(GraphTypePicker); }
        catch
        {
            subscriptions.Dispose();
            ValueScaleFlyout = null;
            throw;
        }
    }
}
