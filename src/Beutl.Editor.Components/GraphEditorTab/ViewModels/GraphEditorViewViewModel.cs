using System.Collections;
using System.Collections.Specialized;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

using Beutl.Animation;

using Reactive.Bindings;

namespace Beutl.Editor.Components.GraphEditorTab.ViewModels;

public sealed class GraphEditorViewViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = [];
    private readonly ConvertToDelegate? _convertTo;
    private readonly TryConvertFromDelegate? _convertFrom;
    private readonly ImmutableSolidColorBrush? _specifiedColor;
    private readonly HashSet<IKeyFrame> _selectedKeyFrames = [];

    public delegate double ConvertToDelegate(object? obj);
    public delegate bool TryConvertFromDelegate(object? oldValue, double value, Type type, out object? obj);

    public GraphEditorViewViewModel(
        GraphEditorViewModel parent,
        string? viewName = null,
        ConvertToDelegate? convertTo = null,
        TryConvertFromDelegate? convertFrom = null,
        Color? color = null)
    {
        Parent = parent;
        Name = viewName;
        _convertTo = convertTo;
        _convertFrom = convertFrom;

        AddKeyFrames();
        Parent.Animation.KeyFrames.CollectionChanged += OnKeyFramesCollectionChanged;

        IsSelected = parent.SelectedView.Select(x => x == this)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        _specifiedColor = new ImmutableSolidColorBrush(color ?? Color.Parse(viewName switch
        {
            "Y" or "Height" or "Green" => "#56B88B",
            "Z" or "Blue" => "#619FEF",
            "W" or "Alpha" => "#BE83E8",
            _ => "#E87070"
        }));
        Stroke = Observable.ReturnThenNever<IBrush?>(_specifiedColor).ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);
    }

    public GraphEditorViewModel Parent { get; }

    public string? Name { get; }

    public string DisplayName => GetDisplayName(Name);

    internal static string GetDisplayName(string? name) => name switch
    {
        "Self" => Strings.GraphValue,
        "Width" => Strings.Width,
        "Height" => Strings.Height,
        "Red" => Strings.Red,
        "Green" => Strings.Green,
        "Blue" => Strings.Blue,
        "Alpha" => Strings.GraphAlpha,
        _ => name ?? Strings.GraphValue
    };

    public ReadOnlyReactivePropertySlim<bool> IsSelected { get; }

    public CoreList<GraphEditorKeyFrameViewModel> KeyFrames { get; } = [];

    public ReactivePropertySlim<int> SelectionCount { get; } = new();

    public ReadOnlyReactivePropertySlim<IBrush?> Stroke { get; }

    public event EventHandler? VerticalRangeChanged;

    public event EventHandler? SelectionChanged;

    internal void NotifyGraphChanged() => VerticalRangeChanged?.Invoke(this, EventArgs.Empty);

    internal bool IsKeyFrameSelected(IKeyFrame keyFrame) => _selectedKeyFrames.Contains(keyFrame);

    public void SetSelection(IEnumerable<IKeyFrame> keyFrames)
    {
        var selection = keyFrames.ToHashSet();
        _selectedKeyFrames.Clear();
        foreach (GraphEditorKeyFrameViewModel item in KeyFrames)
        {
            item.IsSelected.Value = selection.Contains(item.Model);
            if (item.IsSelected.Value)
                _selectedKeyFrames.Add(item.Model);
        }
        SelectionCount.Value = _selectedKeyFrames.Count;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public double ConvertToDouble(object? value)
    {
        if (_convertTo != null)
        {
            return _convertTo(value);
        }

        try
        {
            return Convert.ToDouble(value);
        }
        catch
        {
            return 1d;
        }
    }

    public bool TryConvertFromDouble(object? oldValue, double value, Type type, out object? obj)
    {
        if (_convertFrom != null)
        {
            return _convertFrom(oldValue, value, type, out obj);
        }

        try
        {
            obj = Convert.ChangeType(value, type);
            return true;
        }
        catch
        {
            obj = null;
            return false;
        }
    }

    public void GetGraphRange(ref double min, ref double max, bool selectionOnly = false)
    {
        double low = double.PositiveInfinity, high = double.NegativeInfinity;
        for (int i = 0; i < KeyFrames.Count; i++)
        {
            var item = KeyFrames[i];
            if (!selectionOnly || item.IsSelected.Value)
            {
                double value = Parent.IsSpeedGraph.Value
                    ? GraphEditorCurveMath.KeyVelocity(item) : ConvertToDouble(item.Model.Value);
                low = Math.Min(low, value);
                high = Math.Max(high, value);
            }
            if (i == 0 || selectionOnly && (!item.IsSelected.Value || !KeyFrames[i - 1].IsSelected.Value)) continue;
            var previous = KeyFrames[i - 1].Model;
            double start = ConvertToDouble(previous.Value);
            double difference = ConvertToDouble(item.Model.Value) - start;
            if (Parent.IsSpeedGraph.Value)
            {
                double duration = (item.Model.KeyTime - previous.KeyTime).TotalSeconds;
                for (int sample = 0; sample <= 128; sample++)
                {
                    double velocity = GraphEditorCurveMath.Velocity(item.Model.Easing, sample / 128d, difference, duration);
                    low = Math.Min(low, velocity);
                    high = Math.Max(high, velocity);
                }
            }
            else if (item.Model.Easing is Beutl.Animation.Easings.SplineEasing spline)
            {
                double first = start + spline.Y1 * difference;
                double second = start + spline.Y2 * difference;
                low = Math.Min(low, Math.Min(first, second));
                high = Math.Max(high, Math.Max(first, second));
            }
        }
        min = Math.Min(min, low);
        max = Math.Max(max, high);
    }

    public void GetVerticalRange(ref double min, ref double max)
    {
        foreach (GraphEditorKeyFrameViewModel item in KeyFrames)
        {
            max = Math.Max(max, item.EndY.Value);
            min = Math.Min(min, item.EndY.Value);

            if (item.IsSplineEasing.Value)
            {
                double maxY = Math.Max(item.EndY.Value, item.StartY.Value);

                double c1 = item.ControlPoint1.Value.Y;
                double c2 = item.ControlPoint2.Value.Y;
                c1 = -c1 + maxY;
                c2 = -c2 + maxY;

                max = Math.Max(max, c1);
                min = Math.Min(min, c1);
                max = Math.Max(max, c2);
                min = Math.Min(min, c2);
            }
        }
    }

    private void AddKeyFrames()
    {
        GraphEditorKeyFrameViewModel? prev = null;
        int index = 0;
        foreach (IKeyFrame item in Parent.Animation.KeyFrames)
        {
            var viewModel = new GraphEditorKeyFrameViewModel(item, this);
            viewModel.EndY.Subscribe(_ => VerticalRangeChanged?.Invoke(this, EventArgs.Empty));
            viewModel.ControlPoint1.Subscribe(_ => VerticalRangeChanged?.Invoke(this, EventArgs.Empty));
            viewModel.ControlPoint2.Subscribe(_ => VerticalRangeChanged?.Invoke(this, EventArgs.Empty));
            viewModel.SetPrevious(prev);
            KeyFrames.Insert(index++, viewModel);
            prev = viewModel;
        }
    }

    private void OnKeyFramesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        GraphEditorKeyFrameViewModel? TryGet(int index)
        {
            if (0 <= index && index < KeyFrames.Count)
            {
                return KeyFrames[index];
            }
            else
            {
                return null;
            }
        }

        void UpdateLast()
        {
            if (KeyFrames.Count > 0)
            {
                KeyFrames[^1].SetLast();
            }
        }

        // | NewItem 1 | NewItem 2 | NewItem 3 | Existing | ...
        //          ^     /     ^     /     ^     /
        //           \---/       \---/       \---/
        void Add(int index, IList items)
        {
            foreach (IKeyFrame item in items)
            {
                var viewModel = new GraphEditorKeyFrameViewModel(item, this);
                viewModel.EndY.Subscribe(_ => VerticalRangeChanged?.Invoke(this, EventArgs.Empty));
                viewModel.ControlPoint1.Subscribe(_ => VerticalRangeChanged?.Invoke(this, EventArgs.Empty));
                viewModel.ControlPoint2.Subscribe(_ => VerticalRangeChanged?.Invoke(this, EventArgs.Empty));
                viewModel.SetPrevious(TryGet(index - 1));
                KeyFrames.Insert(index, viewModel);
                index++;
            }

            GraphEditorKeyFrameViewModel? existing = TryGet(index);
            existing?.SetPrevious(TryGet(index - 1));
            UpdateLast();
        }

        // |  Existing | OldItem 1 | OldItem 2 | Existing | ...
        //          ^                             /
        //           \---------------------------/
        void Remove(int index, int count)
        {
            for (int i = 0; i < count; ++i)
            {
                KeyFrames[index + i].Dispose();
            }

            KeyFrames.RemoveRange(index, count);

            GraphEditorKeyFrameViewModel? existing = TryGet(index);
            existing?.SetPrevious(TryGet(index - 1));
            UpdateLast();
        }

        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                Add(e.NewStartingIndex, e.NewItems!);
                break;

            case NotifyCollectionChangedAction.Move:
            case NotifyCollectionChangedAction.Replace:
                if (e.Action == NotifyCollectionChangedAction.Replace)
                    foreach (IKeyFrame item in e.OldItems!) _selectedKeyFrames.Remove(item);
                Remove(e.OldStartingIndex, e.OldItems!.Count);
                Add(e.NewStartingIndex, e.NewItems!);
                break;

            case NotifyCollectionChangedAction.Remove:
                foreach (IKeyFrame item in e.OldItems!)
                    _selectedKeyFrames.Remove(item);
                Remove(e.OldStartingIndex, e.OldItems!.Count);
                break;

            case NotifyCollectionChangedAction.Reset:
                _selectedKeyFrames.Clear();
                Remove(0, KeyFrames.Count);
                break;
        }
        SelectionCount.Value = _selectedKeyFrames.Count;
    }

    public void Dispose()
    {
        Parent.Animation.KeyFrames.CollectionChanged -= OnKeyFramesCollectionChanged;
        foreach (var item in KeyFrames) item.Dispose();
        SelectionCount.Dispose();
        _disposables.Dispose();
    }
}
