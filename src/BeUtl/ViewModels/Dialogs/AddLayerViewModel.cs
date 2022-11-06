using Beutl.Media;
using Beutl.Models;
using Beutl.ProjectSystem;
using Beutl.Streaming;

using Reactive.Bindings;

using AColor = Avalonia.Media.Color;

namespace Beutl.ViewModels.Dialogs;

public sealed class AddLayerViewModel
{
    private readonly Scene _scene;
    private readonly LayerDescription _layerDescription;

    public AddLayerViewModel(Scene scene, LayerDescription desc)
    {
        _scene = scene;
        _layerDescription = desc;

        Color.Value = (desc.InitialOperator == null ? Colors.Teal : desc.InitialOperator.AccentColor).ToAvalonia();
        Layer.Value = desc.Layer;
        Start.Value = desc.Start;
        Duration.Value = desc.Length;
        Layer.SetValidateNotifyError(layer =>
        {
            if (layer < 0)
            {
                return S.Warning.ValueLessThanZero;
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
                return S.Warning.ValueLessThanZero;
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
                return S.Warning.ValueLessThanOrEqualToZero;
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
                return layer >= 0 &&
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
                FileName = Helper.RandomLayerFileName(Path.GetDirectoryName(_scene.FileName)!, Constants.LayerFileExtension)
            };

            if (_layerDescription.InitialOperator != null)
            {
                sLayer.AddChild((StreamOperator)Activator.CreateInstance(_layerDescription.InitialOperator.Type)!)
                    .DoAndRecord(CommandRecorder.Default);
            }

            sLayer.Save(sLayer.FileName);
            _scene.AddChild(sLayer).DoAndRecord(CommandRecorder.Default);
        });
    }

    public ReactivePropertySlim<string> Name { get; } = new();

    public ReactivePropertySlim<AColor> Color { get; } = new();

    public ReactiveProperty<TimeSpan> Start { get; } = new();

    public ReactiveProperty<TimeSpan> Duration { get; } = new();

    public ReactiveProperty<int> Layer { get; } = new();

    public ReadOnlyReactivePropertySlim<bool> CanAdd { get; }

    public ReactiveCommand Add { get; }
}
