using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Beutl.Editor.Components.Helpers;
using Beutl.Engine;
using Beutl.Graphics;

namespace Beutl.Editor.Components.CurvesTab.ViewModels;

public sealed class CurvePresenterViewModel : IDisposable
{
    private readonly IProperty<CurveMap> _property;
    private readonly HistoryManager _history;
    private bool _isUpdating;

    public CurvePresenterViewModel(IProperty<CurveMap> property, HistoryManager history)
    {
        _property = property;
        _history = history;

        Points = new ObservableCollection<CurveControlPoint>(_property.Value.Points);
        Points.CollectionChanged += OnCollectionChanged;
        _property.ValueChanged += OnPropertyChanged;
    }

    public ObservableCollection<CurveControlPoint> Points { get; }

    // The view's drag on this curve; its writes go to the drag's edit session.
    internal DragSession? Drag { get; set; }

    public void EndEdit()
    {
        _history.Commit(CommandNames.EditProperty);
    }

    // Viewからの変更を反映
    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_isUpdating) return;

        _isUpdating = true;
        try
        {
            var map = new CurveMap(Points);
            using (Drag?.Enter())
            {
                _property.Value = map;
            }
        }
        finally
        {
            _isUpdating = false;
        }
    }

    // Modelからの変更を反映
    private void OnPropertyChanged(object? sender, PropertyValueChangedEventArgs<CurveMap> e)
    {
        if (_isUpdating) return;

        _isUpdating = true;
        try
        {
            Points.Clear();
            foreach (var point in _property.Value.Points)
            {
                Points.Add(point);
            }
        }
        finally
        {
            _isUpdating = false;
        }
    }

    public void Dispose()
    {
        Points.CollectionChanged -= OnCollectionChanged;
        _property.ValueChanged -= OnPropertyChanged;
    }
}
