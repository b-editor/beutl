using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;

namespace Beutl.ViewModels.Tools;

public sealed class CurvePresenterViewModel : IDisposable
{
    private readonly Curves _effect;
    private readonly IProperty<CurveMap> _property;
    private readonly CommandRecorder _commandRecorder;
    private CurveMap? _oldValue;
    private bool _isUpdating;

    public CurvePresenterViewModel(string header, Curves effect, IProperty<CurveMap> property,
        CommandRecorder commandRecorder)
    {
        Header = header;
        _effect = effect;
        _property = property;
        _commandRecorder = commandRecorder;

        Points = new ObservableCollection<CurveControlPoint>(_property.CurrentValue.Points);
        Points.CollectionChanged += OnCollectionChanged;
        _property.ValueChanged += OnPropertyChanged;
    }

    public string Header { get; }

    public ObservableCollection<CurveControlPoint> Points { get; }

    public void BeginEdit()
    {
        _oldValue = _property.CurrentValue;
    }

    public void EndEdit()
    {
        if (_oldValue == null) return;

        CurveMap newValue = _property.CurrentValue;
        CurveMap oldValue = _oldValue;
        _oldValue = null;

        if (oldValue != newValue)
        {
            RecordableCommands.Edit(_property, newValue, oldValue)
                .WithStoables([_effect])
                .DoAndRecord(_commandRecorder);
        }
    }

    // Viewからの変更を反映
    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_isUpdating) return;

        _isUpdating = true;
        try
        {
            var map = new CurveMap(Points);
            _property.CurrentValue = map;
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
            Points.CollectionChanged -= OnCollectionChanged;
            Points.Clear();
            foreach (var point in _property.CurrentValue.Points)
            {
                Points.Add(point);
            }
            Points.CollectionChanged += OnCollectionChanged;
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
