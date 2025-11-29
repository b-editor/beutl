using Beutl.Media;
using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;

using AM = Avalonia.Media;

namespace Beutl.ViewModels.Editors;

public class GradientStopsEditorViewModel : BaseEditorViewModel<ICoreList<GradientStop>>
{
    private IDisposable? _disposable;

    public GradientStopsEditorViewModel(IPropertyAdapter<ICoreList<GradientStop>> property)
        : base(property)
    {
        Value = property.GetObservable()!
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables)!;

        Value.Subscribe(v =>
        {
            _disposable?.Dispose();

            var t = v.ToAvaGradientStopsSync(CurrentTime);
            _disposable = t.Item2;
            Stops.Value = t.Item1;
        }).DisposeWith(Disposables);
    }

    public ReadOnlyReactivePropertySlim<ICoreList<GradientStop>> Value { get; }

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
        GradientStop.Resource oldObject, GradientStop obj)
    {
        CommandRecorder recorder = this.GetRequiredService<CommandRecorder>();
        if (Value.Value is { } list)
        {
            IRecordableCommand? move = oldIndex == newIndex ? null : list.BeginRecord<GradientStop>()
                .Move(oldIndex, newIndex)
                .ToCommand([]);

            IRecordableCommand? offset = obj.Offset.CurrentValue != oldObject.Offset
                ? RecordableCommands.Edit(obj.Offset, obj.Offset.CurrentValue, oldObject.Offset)
                : null;
            IRecordableCommand? color = obj.Color.CurrentValue != oldObject.Color
                ? RecordableCommands.Edit(obj.Color, obj.Color.CurrentValue, oldObject.Color)
                : null;

            move.Append(offset)
                .Append(color)
                .WithStoables(GetStorables())
                .DoAndRecord(recorder);
        }
    }

}
