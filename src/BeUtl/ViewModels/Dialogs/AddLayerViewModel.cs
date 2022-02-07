using System.Reactive.Linq;

using Avalonia;
using Avalonia.Controls;

using BeUtl.Media;
using BeUtl.Models;
using BeUtl.ProjectSystem;

using FluentAvalonia.UI.Media;

using OpenCvSharp;

using Reactive.Bindings;

namespace BeUtl.ViewModels.Dialogs;

public sealed class AddLayerViewModel
{
    private readonly Scene _scene;
    private readonly LayerDescription _layerDescription;

    public AddLayerViewModel(Scene scene, LayerDescription desc)
    {
        _scene = scene;
        _layerDescription = desc;

        Color.Value = (desc.InitialOperation == null ? Colors.Teal : desc.InitialOperation.AccentColor).ToAvalonia();
        Layer.Value = desc.Layer;
        Start.Value = desc.Start;
        Duration.Value = desc.Length;
        Layer.SetValidateNotifyError(layer =>
        {
            if (ExistsLayer(layer))
            {
                return (string?)Application.Current?.FindResource("ThisLayerNumberIsAlreadyInUseString");
            }
            else
            {
                return null;
            }
        });
        Start.SetValidateNotifyError(start =>
        {
            if (start < TimeSpan.Zero)
            {
                return (string?)Application.Current?.FindResource("CannotSpecifyValueLessThanString");
            }
            else
            {
                return null;
            }
        });
        Duration.SetValidateNotifyError(length =>
        {
            if (length <= TimeSpan.Zero)
            {
                return (string?)Application.Current?.FindResource("CannotSpecifyValueLessThanOrEqualToZeroString");
            }
            else
            {
                return null;
            }
        });

        CanAdd = Layer.CombineLatest(Start, Duration)
            .Select(item =>
            {
                (int layer, TimeSpan start, TimeSpan length) = item;
                return !ExistsLayer(layer) &&
                    start >= TimeSpan.Zero &&
                    length > TimeSpan.Zero;
            })
            .ToReadOnlyReactivePropertySlim();

        Add = new(CanAdd);

        Add.Subscribe(() =>
        {
            var sLayer = new Layer()
            {
                Name = Name.Value,
                Start = Start.Value,
                Length = Duration.Value,
                ZIndex = Layer.Value,
                AccentColor = new(Color.Value.A, Color.Value.R, Color.Value.G, Color.Value.B),
                FileName = Helper.RandomLayerFileName(Path.GetDirectoryName(_scene.FileName)!, "layer")
            };

            if (_layerDescription.InitialOperation != null)
            {
                sLayer.AddChild((LayerOperation)(Activator.CreateInstance(_layerDescription.InitialOperation.Type)!))
                    .DoAndRecord(CommandRecorder.Default);
            }

            sLayer.Save(sLayer.FileName);
            _scene.AddChild(sLayer).DoAndRecord(CommandRecorder.Default);
        });
    }

    public ReactivePropertySlim<string> Name { get; } = new();

    public ReactivePropertySlim<Color2> Color { get; } = new();

    public ReactiveProperty<TimeSpan> Start { get; } = new();

    public ReactiveProperty<TimeSpan> Duration { get; } = new();

    public ReactiveProperty<int> Layer { get; } = new();

    public ReadOnlyReactivePropertySlim<bool> CanAdd { get; }

    public ReactiveCommand Add { get; }

    private bool ExistsLayer(int layer)
    {
        foreach (Layer item in _scene.Children)
        {
            if (item.ZIndex == layer)
            {
                return true;
            }
        }

        return false;
    }
}
