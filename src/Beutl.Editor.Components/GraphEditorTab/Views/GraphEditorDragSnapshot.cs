using Avalonia;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Editor.Components.GraphEditorTab.ViewModels;
using Beutl.Editor.Components.Helpers;

namespace Beutl.Editor.Components.GraphEditorTab.Views;

// Keep model identities, rather than item view models, because retiming reorders the collection.
internal sealed class GraphEditorDragSnapshot
{
    private readonly GraphEditorViewViewModel _channel;
    private readonly Dictionary<IKeyFrame, (Point? Incoming, Point? Outgoing)> _handles = [];
    private readonly Dictionary<IKeyFrame, Easing> _originalEasings = [];
    private readonly Dictionary<(IKeyFrame Start, IKeyFrame End), Easing?> _reversedSegments = [];
    internal readonly record struct Entry(IKeyFrame Model, TimeSpan Time, object? Value, double Number);
    public Entry[] Entries { get; }
    public double InitialBaseline { get; }
    public Dictionary<IKeyFrame, (double Incoming, double Outgoing)> Velocities { get; } = [];

    public GraphEditorDragSnapshot(GraphEditorViewViewModel channel)
    {
        _channel = channel;
        InitialBaseline = channel.Parent.Baseline.Value;
        Entries = channel.KeyFrames.Where(x => x.IsSelected.Value)
            .Select(x => new Entry(x.Model, x.Model.KeyTime, x.Model.Value, channel.ConvertToDouble(x.Model.Value)))
            .ToArray();
        foreach (var key in channel.KeyFrames)
            _originalEasings[key.Model] = key.Model.Easing;
        foreach (var key in channel.KeyFrames.Where(x => x.IsSelected.Value))
            Velocities[key.Model] = (GraphEditorView.GetKeyVelocity(key), GraphEditorView.GetKeyVelocity(key, true));
        for (int i = 1; i < channel.KeyFrames.Count; i++)
        {
            var item = channel.KeyFrames[i];
            var previous = channel.KeyFrames[i - 1];
            Easing original = item.Model.Easing;
            _reversedSegments[(item.Model, previous.Model)] = ReverseEasing(original);
            if (original is not SplineEasing spline) continue;
            double duration = (item.Model.KeyTime - previous.Model.KeyTime).TotalSeconds;
            double difference = channel.ConvertToDouble(item.Model.Value) - channel.ConvertToDouble(previous.Model.Value);
            var incoming = new Point((spline.X2 - 1) * duration, (spline.Y2 - 1) * difference);
            var outgoing = new Point(spline.X1 * duration, spline.Y1 * difference);
            _handles[item.Model] = (incoming, _handles.GetValueOrDefault(item.Model).Outgoing);
            _handles[previous.Model] = (_handles.GetValueOrDefault(previous.Model).Incoming, outgoing);
        }
    }

    public void Apply(Func<Entry, (double Time, double Value)> transform, double timeScale = 1, double valueScale = 1, bool transformHandles = false)
    {
        if (Entries.Length == 0) return;
        var editor = _channel.Parent;
        int rate = editor.Scene.FindHierarchicalParent<Project>()?.GetFrameRate() ?? 30;
        var changes = Entries.Select(entry => (Entry: entry, Result: transform(entry))).ToArray();
        if (changes.Any(change => !double.IsFinite(change.Result.Time))) return;
        // Clamp the group as a whole so its spacing is preserved at time zero.
        double correction = Math.Max(0, -changes.Min(x => x.Result.Time));
        var times = changes.Select(change => TimeSpan.FromSeconds(change.Result.Time + correction).RoundToRate(rate)).ToArray();
        var selected = Entries.Select(x => x.Model).ToHashSet();
        // Only selected segments are mirrored. Reject unsupported interpolation before any
        // part of the transform changes the animation, retaining the last valid preview.
        if (timeScale < 0 && _reversedSegments.Any(segment => segment.Value == null
                && selected.Contains(segment.Key.Start) && selected.Contains(segment.Key.End))) return;
        var occupied = editor.Animation.KeyFrames.Where(key => !selected.Contains(key)).Select(key => key.KeyTime).ToHashSet();
        // Reject the whole proposal before changing values or tangents. A drag can continue
        // past the occupied frame while retaining its last valid state at a collision.
        if (times.Distinct().Count() != times.Length || times.Any(occupied.Contains)) return;
        foreach (var (entry, result) in changes)
        {
            double number = editor.Factory is { } factory
                ? Math.Clamp(result.Value, factory.MinValue, factory.MaxValue) : result.Value;
            if (double.IsFinite(number)
                && _channel.TryConvertFromDouble(entry.Value, number, editor.Animation.ValueType, out var value))
                entry.Model.Value = value;
        }
        foreach (var (entry, time) in changes.Select((change, index) => (change.Entry, Time: times[index])).OrderByDescending(x => x.Time))
        {
            entry.Model.KeyTime = time;
        }

        var handles = new Dictionary<IKeyFrame, (Point? Incoming, Point? Outgoing)>();
        Point Scale(Point point) => new(point.X * timeScale, point.Y * valueScale);
        foreach (var (key, original) in _handles)
        {
            if (!selected.Contains(key))
            {
                handles[key] = original;
                continue;
            }
            Point? incoming = timeScale < 0 ? original.Outgoing : original.Incoming;
            Point? outgoing = timeScale < 0 ? original.Incoming : original.Outgoing;
            handles[key] = (incoming is { } left ? Scale(left) : null, outgoing is { } right ? Scale(right) : null);
        }
        if (transformHandles)
        {
            // Only handles inside the selected span are directly scaled. The other handle
            // at a boundary follows the same coupling rules as a direct handle drag.
            int firstSelected = _channel.KeyFrames.IndexOf(_channel.KeyFrames.First(key => selected.Contains(key.Model)));
            int lastSelected = _channel.KeyFrames.IndexOf(_channel.KeyFrames.Last(key => selected.Contains(key.Model)));
            for (int i = 0; i < _channel.KeyFrames.Count; i++)
            {
                var key = _channel.KeyFrames[i].Model;
                if (!selected.Contains(key) || !_handles.TryGetValue(key, out var original)) continue;
                bool incomingSelected = i > firstSelected;
                bool outgoingSelected = i < lastSelected;
                var pair = handles[key];
                if (outgoingSelected && !incomingSelected && original.Incoming is { } before && pair.Outgoing is { } outgoing)
                    pair.Incoming = GraphEditorTangentCoupling.Opposite(editor, outgoing, before);
                if (incomingSelected && !outgoingSelected && original.Outgoing is { } after && pair.Incoming is { } incoming)
                    pair.Outgoing = GraphEditorTangentCoupling.Opposite(editor, incoming, after);
                handles[key] = pair;
            }
        }
        for (int i = 1; i < _channel.KeyFrames.Count; i++)
        {
            var item = _channel.KeyFrames[i];
            var previous = _channel.KeyFrames[i - 1];
            if (!selected.Contains(item.Model) && !selected.Contains(previous.Model)) continue;
            if (timeScale < 0 && selected.Contains(item.Model) && selected.Contains(previous.Model)
                && _reversedSegments.TryGetValue((previous.Model, item.Model), out var reversed) && reversed != null)
                item.Model.Easing = reversed;
            else if (_originalEasings.TryGetValue(item.Model, out var original))
                item.Model.Easing = original;
            if (item.Model.Easing is not SplineEasing spline) continue;
            double duration = (item.Model.KeyTime - previous.Model.KeyTime).TotalSeconds;
            double difference = _channel.ConvertToDouble(item.Model.Value) - _channel.ConvertToDouble(previous.Model.Value);
            if (handles.GetValueOrDefault(previous.Model).Outgoing is { } first)
                SetHandle(spline, false, first, duration, difference);
            if (handles.GetValueOrDefault(item.Model).Incoming is { } second)
                SetHandle(spline, true, second, duration, difference);
        }
    }

    // Keep built-in easing types so the mirrored animation uses the existing serialization format.
    private static Easing? ReverseEasing(Easing easing) => easing switch
    {
        SplineEasing spline when spline.GetType() == typeof(SplineEasing)
            => new SplineEasing(1 - spline.X2, 1 - spline.Y2, 1 - spline.X1, 1 - spline.Y1),
        QuadraticEaseIn => new QuadraticEaseOut(),
        QuadraticEaseOut => new QuadraticEaseIn(),
        CubicEaseIn => new CubicEaseOut(),
        CubicEaseOut => new CubicEaseIn(),
        QuarticEaseIn => new QuarticEaseOut(),
        QuarticEaseOut => new QuarticEaseIn(),
        QuinticEaseIn => new QuinticEaseOut(),
        QuinticEaseOut => new QuinticEaseIn(),
        SineEaseIn => new SineEaseOut(),
        SineEaseOut => new SineEaseIn(),
        CircularEaseIn => new CircularEaseOut(),
        CircularEaseOut => new CircularEaseIn(),
        ExponentialEaseIn => new ExponentialEaseOut(),
        ExponentialEaseOut => new ExponentialEaseIn(),
        ElasticEaseIn => new ElasticEaseOut(),
        ElasticEaseOut => new ElasticEaseIn(),
        BackEaseIn => new BackEaseOut(),
        BackEaseOut => new BackEaseIn(),
        BounceEaseIn => new BounceEaseOut(),
        BounceEaseOut => new BounceEaseIn(),
        LinearEasing or QuadraticEaseInOut or CubicEaseInOut or QuarticEaseInOut or QuinticEaseInOut
            or SineEaseInOut or CircularEaseInOut or ExponentialEaseInOut or ElasticEaseInOut
            or BackEaseInOut or BounceEaseInOut => easing,
        _ => null
    };

    private static void SetHandle(SplineEasing spline, bool incoming, Point vector,
        double duration, double difference)
    {
        double origin = incoming ? 1 : 0;
        double x = origin + vector.X / duration;
        double y = origin + vector.Y / difference;
        if (double.IsFinite(x))
        {
            if (incoming) spline.X2 = (float)Math.Clamp(x, 0, 1);
            else spline.X1 = (float)Math.Clamp(x, 0, 1);
        }
        if (double.IsFinite(y) && Math.Abs(y) <= float.MaxValue)
        {
            if (incoming) spline.Y2 = (float)y;
            else spline.Y1 = (float)y;
        }
    }
}
