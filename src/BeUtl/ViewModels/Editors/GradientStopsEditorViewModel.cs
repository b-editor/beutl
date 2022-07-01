using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BeUtl.Commands;
using BeUtl.Media;
using BeUtl.Services.Editors.Wrappers;
using BeUtl.Utilities;

using Reactive.Bindings;

using AM = Avalonia.Media;

namespace BeUtl.ViewModels.Editors;

public class GradientStopsEditorViewModel : BaseEditorViewModel<GradientStops>
{
    private IDisposable? _disposable;

    public GradientStopsEditorViewModel(IWrappedProperty<GradientStops> property)
        : base(property)
    {
        GradientStops? initValue = property.GetValue();
        if (initValue == null)
        {
            property.SetValue(initValue = new GradientStops());
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

        IsSelected = SelectedItem.Select(x => x != null)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);
    }

    public ReadOnlyReactivePropertySlim<GradientStops> Value { get; }

    public AM.GradientStops Stops { get; } = new();

    public ReactivePropertySlim<AM.GradientStop?> SelectedItem { get; } = new();

    public ReadOnlyReactivePropertySlim<bool> IsSelected { get; }

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
                command = new ChangePropertyCommand<Color>(model, GradientStop.ColorProperty, newColor, oldColor2);
            }

            if (!MathUtilities.AreClose(model.Offset, oldOffset2))
            //if (model.Offset != oldOffset2)
            {
                var tmp = new ChangePropertyCommand<float>(model, GradientStop.OffsetProperty, newOffset, oldOffset2);
                command = command == null ? tmp : command.Append(tmp);
            }

            if (command != null)
            {
                command.DoAndRecord(CommandRecorder.Default);
            }
        }
    }

    public void AddItem(GradientStop stop, int index = -1)
    {
        new AddCommand<GradientStop>(Value.Value, stop, index < 0 ? Value.Value.Count : index).DoAndRecord(CommandRecorder.Default);
    }

    public void RemoveItem(AM.GradientStop stop)
    {
        int index = Stops.IndexOf(stop);
        if (index >= 0)
        {
            GradientStop model = Value.Value[index];
            new RemoveCommand<GradientStop>(Value.Value, model).DoAndRecord(CommandRecorder.Default);

            if (stop == SelectedItem.Value)
            {
                SelectedItem.Value = null;
            }
        }
    }
}
