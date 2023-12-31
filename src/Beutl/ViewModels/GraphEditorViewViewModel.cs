using System.Collections;
using System.Collections.Specialized;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

using Beutl.Animation;

using Reactive.Bindings;

namespace Beutl.ViewModels;

public sealed class GraphEditorViewViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = [];
    private readonly ConvertToDelegate? _convertTo;
    private readonly TryConvertFromDelegate? _convertFrom;
    private readonly ImmutableSolidColorBrush? _specifiedColor;

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

        if (color.HasValue)
        {
            _specifiedColor = new ImmutableSolidColorBrush(color.Value);
        }

        Stroke = Application.Current!.GetResourceObservable("TextControlForeground")
            .Select(x => _specifiedColor ?? (x as IBrush))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);
    }

    public GraphEditorViewModel Parent { get; }

    public string? Name { get; }

    public ReadOnlyReactivePropertySlim<bool> IsSelected { get; }

    public CoreList<GraphEditorKeyFrameViewModel> KeyFrames { get; } = [];

    public ReadOnlyReactivePropertySlim<IBrush?> Stroke { get; }

    public event EventHandler? VerticalRangeChanged;

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
        }

        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                Add(e.NewStartingIndex, e.NewItems!);
                break;

            case NotifyCollectionChangedAction.Move:
            case NotifyCollectionChangedAction.Replace:
                Remove(e.OldStartingIndex, e.OldItems!.Count);
                Add(e.NewStartingIndex, e.NewItems!);
                break;

            case NotifyCollectionChangedAction.Remove:
                Remove(e.OldStartingIndex, e.OldItems!.Count);
                break;

            case NotifyCollectionChangedAction.Reset:
                Remove(0, KeyFrames.Count);
                break;
        }
    }

    public void Dispose()
    {
        Parent.Animation.KeyFrames.CollectionChanged -= OnKeyFramesCollectionChanged;
        IsSelected.Dispose();
    }
}
