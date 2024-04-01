using System.Collections.Immutable;

using Beutl.Commands;
using Beutl.Media;
using Beutl.Media.Immutable;
using Beutl.Utilities;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;

using AM = Avalonia.Media;

namespace Beutl.ViewModels.Editors;

public class GradientStopsEditorViewModel : BaseEditorViewModel<GradientStops>
{
    private IDisposable? _disposable;

    public GradientStopsEditorViewModel(IAbstractProperty<GradientStops> property)
        : base(property)
    {
        GradientStops? initValue = property.GetValue();
        if (initValue == null)
        {
            property.SetValue(initValue = []);
        }

        Value = property.GetObservable()
            .ToReadOnlyReactivePropertySlim(initValue)
            .DisposeWith(Disposables)!;

        Value.Subscribe(v =>
        {
            _disposable?.Dispose();

            var t = v.ToAvaGradientStopsSync();
            _disposable = t.Item2;
            Stops.Value = t.Item1;
        }).DisposeWith(Disposables);
    }

    public ReadOnlyReactivePropertySlim<GradientStops> Value { get; }

    public ReactivePropertySlim<AM.GradientStops> Stops { get; } = new();

    public ReactivePropertySlim<AM.GradientStop?> SelectedItem { get; } = new();

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _disposable?.Dispose();
    }

    public void InsertGradientStop(int index, GradientStop item)
    {
        if (Value.Value is { } list)
        {
            CommandRecorder recorder = this.GetRequiredService<CommandRecorder>();
            list.BeginRecord<GradientStop>()
                .Insert(index, item)
                .ToCommand(GetStorables())
                .DoAndRecord(recorder);
        }
    }

    public void RemoveGradientStop(int index)
    {
        if (Value.Value is { } list)
        {
            CommandRecorder recorder = this.GetRequiredService<CommandRecorder>();
            list.BeginRecord<GradientStop>()
                .RemoveAt(index)
                .ToCommand(GetStorables())
                .DoAndRecord(recorder);
        }
    }

    public void ConfirmeGradientStop(
        int oldIndex, int newIndex,
        ImmutableGradientStop oldObject, GradientStop obj)
    {
        CommandRecorder recorder = this.GetRequiredService<CommandRecorder>();
        if (Value.Value is { } list)
        {
            IRecordableCommand? move = oldIndex == newIndex ? null : list.BeginRecord<GradientStop>()
                .Move(oldIndex, newIndex)
                .ToCommand([]);

            IRecordableCommand? offset = obj.Offset != oldObject.Offset
                ? RecordableCommands.Edit(obj, GradientStop.OffsetProperty, obj.Offset, oldObject.Offset)
                : null;
            IRecordableCommand? color = obj.Color != oldObject.Color
                ? RecordableCommands.Edit(obj, GradientStop.ColorProperty, obj.Color, oldObject.Color)
                : null;

            move.Append(offset)
                .Append(color)
                .WithStoables(GetStorables())
                .DoAndRecord(recorder);
        }
    }

}
