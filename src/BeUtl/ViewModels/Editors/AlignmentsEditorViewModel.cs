using System.Text.Json.Nodes;

using Beutl.Framework;
using Beutl.Media;
using Beutl.Services.PrimitiveImpls;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.ViewModels.Editors;

public sealed class AlignmentsEditorViewModel : IPropertyEditorContext
{
    private readonly CompositeDisposable _disposables = new();
    private readonly IAbstractProperty<AlignmentX> _xProperty;
    private readonly IAbstractProperty<AlignmentY> _yProperty;

    public AlignmentsEditorViewModel(IAbstractProperty<AlignmentX> xProperty, IAbstractProperty<AlignmentY> yProperty)
    {
        _xProperty = xProperty;
        _yProperty = yProperty;

        X = xProperty.GetObservable()
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);

        IsLeft = xProperty.GetObservable()
            .Select(x => x is AlignmentX.Left)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);

        IsHorizontalCenter = xProperty.GetObservable()
            .Select(x => x is AlignmentX.Center)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);

        IsRight = xProperty.GetObservable()
            .Select(x => x is AlignmentX.Right)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);

        Y = yProperty.GetObservable()
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);

        IsTop = yProperty.GetObservable()
            .Select(y => y is AlignmentY.Top)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);

        IsVerticalCenter = yProperty.GetObservable()
            .Select(y => y is AlignmentY.Center)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);

        IsBottom = yProperty.GetObservable()
            .Select(y => y is AlignmentY.Bottom)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);
    }

    public ReadOnlyReactivePropertySlim<AlignmentX> X { get; }

    public ReadOnlyReactivePropertySlim<bool> IsLeft { get; }

    public ReadOnlyReactivePropertySlim<bool> IsHorizontalCenter { get; }

    public ReadOnlyReactivePropertySlim<bool> IsRight { get; }

    public ReadOnlyReactivePropertySlim<AlignmentY> Y { get; }

    public ReadOnlyReactivePropertySlim<bool> IsTop { get; }

    public ReadOnlyReactivePropertySlim<bool> IsVerticalCenter { get; }

    public ReadOnlyReactivePropertySlim<bool> IsBottom { get; }

    public PropertyEditorExtension Extension
    {
        get => AlignmentsPropertyEditorExtension.Instance;
        set { }
    }

    public void Dispose()
    {
        _disposables.Dispose();
    }

    public void ReadFromJson(JsonNode json)
    {
    }

    public void WriteToJson(ref JsonNode json)
    {
    }

    public void Reset()
    {
        CorePropertyMetadata<AlignmentX> xMetadata = _xProperty.Property.GetMetadata<CorePropertyMetadata<AlignmentX>>(_xProperty.ImplementedType);
        CorePropertyMetadata<AlignmentY> yMetadata = _yProperty.Property.GetMetadata<CorePropertyMetadata<AlignmentY>>(_yProperty.ImplementedType);

        AlignmentX newX = xMetadata.HasDefaultValue ? xMetadata.DefaultValue : X.Value;
        AlignmentY newY = yMetadata.HasDefaultValue ? yMetadata.DefaultValue : Y.Value;

        new SetValuesCommand(_xProperty, _yProperty, (newX, newY), (X.Value, Y.Value))
            .DoAndRecord(CommandRecorder.Default);
    }

    public void SetAlignmentX(AlignmentX value)
    {
        if (X.Value != value)
        {
            new SetValueCommand<AlignmentX>(_xProperty, X.Value, value)
                .DoAndRecord(CommandRecorder.Default);
        }
    }

    public void SetAlignmentY(AlignmentY value)
    {
        if (Y.Value != value)
        {
            new SetValueCommand<AlignmentY>(_yProperty, Y.Value, value)
                .DoAndRecord(CommandRecorder.Default);
        }
    }

    private sealed class SetValuesCommand : IRecordableCommand
    {
        private readonly IAbstractProperty<AlignmentX> _x;
        private readonly IAbstractProperty<AlignmentY> _y;
        private readonly (AlignmentX, AlignmentY) _oldValue;
        private readonly (AlignmentX, AlignmentY) _newValue;

        public SetValuesCommand(IAbstractProperty<AlignmentX> x, IAbstractProperty<AlignmentY> y, (AlignmentX, AlignmentY) oldValue, (AlignmentX, AlignmentY) newValue)
        {
            _x = x;
            _y = y;
            _oldValue = oldValue;
            _newValue = newValue;
        }

        public void Do()
        {
            _x.SetValue(_newValue.Item1);
            _y.SetValue(_newValue.Item2);
        }

        public void Redo()
        {
            Do();
        }

        public void Undo()
        {
            _x.SetValue(_oldValue.Item1);
            _y.SetValue(_oldValue.Item2);
        }
    }

    private sealed class SetValueCommand<T> : IRecordableCommand
    {
        private readonly IAbstractProperty<T> _setter;
        private readonly T? _oldValue;
        private readonly T? _newValue;

        public SetValueCommand(IAbstractProperty<T> setter, T? oldValue, T? newValue)
        {
            _setter = setter;
            _oldValue = oldValue;
            _newValue = newValue;
        }

        public void Do()
        {
            _setter.SetValue(_newValue);
        }

        public void Redo()
        {
            Do();
        }

        public void Undo()
        {
            _setter.SetValue(_oldValue);
        }
    }
}
