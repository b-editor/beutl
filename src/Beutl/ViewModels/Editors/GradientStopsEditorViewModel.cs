using System.Collections.Immutable;

using Beutl.Commands;
using Beutl.Media;
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
            Stops.Clear();
            _disposable = v.ForEachItem(
                (idx, item) => Stops.Insert(idx, new(item.Color.ToAvalonia(), item.Offset)),
                (idx, _) => Stops.RemoveAt(idx),
                () => Stops.Clear());
        }).DisposeWith(Disposables);
    }

    public ReadOnlyReactivePropertySlim<GradientStops> Value { get; }

    public AM.GradientStops Stops { get; } = [];

    public ReactivePropertySlim<AM.GradientStop?> SelectedItem { get; } = new();

    public void PushChange(AM.GradientStop stop)
    {
        int index = Stops.IndexOf(stop);
        if (index >= 0)
        {
            GradientStop? model = Value.Value[index];
            model.Color = stop.Color.ToMedia();
            model.Offset = (float)stop.Offset;
        }
    }

    public void SaveChange(AM.GradientStop stop, AM.Color oldColor, double oldOffset)
    {
        int index = Stops.IndexOf(stop);
        if (index >= 0)
        {
            GradientStop? model = Value.Value[index];
            IRecordableCommand? command = null;
            Color oldColor2 = oldColor.ToMedia();
            Color newColor = stop.Color.ToMedia();
            float oldOffset2 = (float)oldOffset;
            float newOffset = (float)stop.Offset;

            if (model.Color != newColor)
            {
                command = new ChangePropertyCommand<Color>(model, GradientStop.ColorProperty, newColor, oldColor2, []);
            }

            if (!MathUtilities.AreClose(model.Offset, oldOffset2))
            {
                var tmp = new ChangePropertyCommand<float>(model, GradientStop.OffsetProperty, newOffset, oldOffset2, []);
                command = command == null ? tmp : command.Append(tmp);
            }

            GradientStop? prev = index > 0 ? Value.Value[index - 1] : null;
            GradientStop? next = index + 1 < Value.Value.Count ? Value.Value[index + 1] : null;

            if (prev != null && model.Offset < prev.Offset)
            {
                IRecordableCommand tmp = Value.Value.BeginRecord<GradientStop>()
                    .Move(index, index - 1)
                    .ToCommand([]);
                command = command == null ? tmp : command.Append(tmp);
            }
            else if (next != null && next.Offset < model.Offset)
            {
                IRecordableCommand tmp = Value.Value.BeginRecord<GradientStop>()
                    .Move(index, index + 1)
                    .ToCommand([]);
                command = command == null ? tmp : command.Append(tmp);
            }

            CommandRecorder recorder = this.GetRequiredService<CommandRecorder>();
            command
                ?.WithStoables(GetStorables())
                ?.DoAndRecord(recorder);
        }
    }

    public void AddItem(GradientStop stop, int index = -1)
    {
        CommandRecorder recorder = this.GetRequiredService<CommandRecorder>();
        Value.Value.BeginRecord<GradientStop>()
            .Insert(index < 0 ? Value.Value.Count : index, stop)
            .ToCommand(GetStorables())
            .DoAndRecord(recorder);
    }

    public void RemoveItem(AM.GradientStop stop)
    {
        int index = Stops.IndexOf(stop);
        if (index >= 0)
        {
            CommandRecorder recorder = this.GetRequiredService<CommandRecorder>();
            GradientStop model = Value.Value[index];
            Value.Value.BeginRecord<GradientStop>()
                .Remove(model)
                .ToCommand(GetStorables())
                .DoAndRecord(recorder);

            if (stop == SelectedItem.Value)
            {
                SelectedItem.Value = null;
            }
        }
    }
}
